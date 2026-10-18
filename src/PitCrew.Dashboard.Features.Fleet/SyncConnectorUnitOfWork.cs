using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;
using PitCrew.Protocol;

using static PitCrew.Protocol.ManagerObservedStateValidator;

namespace PitCrew.Dashboard.Features.Fleet;

internal enum ConnectorSyncStatus
{
  Accepted,
  Unauthorized,
  Invalid,
}

internal sealed record ConnectorSyncResult(
    ConnectorSyncStatus Status,
    string? Error,
    ConnectorSyncResponse? Response,
    ConnectorSyncRejectionReason RejectionReason = ConnectorSyncRejectionReason.Unspecified);

internal sealed record ConnectorSynchronizationInput(
    int ProtocolVersion,
    string ConnectorVersion,
    DateTimeOffset SentAt,
    IReadOnlyList<ManagerObservedState> Profiles,
    CapacityOperatorCapability? CapacityOperator,
    CapacityCommandOutcome? CapacityCommandOutcome,
    RecoveryOperatorCapability? RecoveryOperator,
    RecoveryCommandProgress? RecoveryCommandProgress,
    RecoveryCommandOutcome? RecoveryCommandOutcome,
    ConnectorHealthReplay? ConnectorHealth = null,
    ImageRolloutOperatorCapability? ImageRolloutOperator = null,
    ImageRolloutCommandProgress? ImageRolloutCommandProgress = null,
    ImageRolloutCommandOutcome? ImageRolloutCommandOutcome = null,
    ConnectorProfileInventory? ProfileInventory = null);

internal interface ISyncConnectorUnitOfWork
{
  Task<ConnectorSyncResult> SynchronizeAsync(
      string credential,
      ConnectorSynchronizationInput input,
      CancellationToken cancellationToken);
}

internal sealed partial class SyncConnectorUnitOfWork(
    IFleetStore _fleetStore,
    IFleetHistoryStore _fleetHistoryStore,
    IConnectorHealthStore _connectorHealthStore,
    IFleetStorageTransactionFactory _transactionFactory,
    ICapacityCommandStore _capacityCommandStore,
    IRecoveryCommandStore _recoveryCommandStore,
    IImageRolloutCommandStore _imageRolloutCommandStore,
    ConnectorCredentialService _credentialService,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : ISyncConnectorUnitOfWork
{
  public async Task<ConnectorSyncResult> SynchronizeAsync(
      string credential,
      ConnectorSynchronizationInput input,
      CancellationToken cancellationToken)
  {
    var identity = await _fleetStore.ResolveNodeOrNullAsync(
        _credentialService.Hash(credential),
        cancellationToken);
    if (identity is null)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Unauthorized,
          null,
          null);
    }

    if (input.ProtocolVersion < PitCrewProtocol.MinimumSupportedVersion ||
        input.ProtocolVersion > PitCrewProtocol.Version)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          $"Unsupported connector protocol version '{input.ProtocolVersion}'.",
          null,
          ConnectorSyncRejectionReason.Protocol);
    }
    if (string.IsNullOrWhiteSpace(input.ConnectorVersion) ||
        input.ConnectorVersion.Length > 128)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Connector version must be between 1 and 128 characters.",
          null,
          ConnectorSyncRejectionReason.ConnectorVersion);
    }
    if (input.Profiles is null ||
        input.Profiles.Count > 256)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "A connector cannot synchronize more than 256 profiles.",
          null,
          ConnectorSyncRejectionReason.ProfileCount);
    }
    if (!IsValidProfileInventory(
        input.ProtocolVersion,
        input.ProfileInventory,
        input.Profiles.Count))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Profile inventory does not satisfy the connector protocol contract.",
          null,
          ConnectorSyncRejectionReason.ProfileInventory);
    }

    var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var profile in input.Profiles)
    {
      if (!IsValidProfile(profile))
      {
        return new ConnectorSyncResult(
            ConnectorSyncStatus.Invalid,
            "A profile does not satisfy the observed-state contract.",
            null,
            ConnectorSyncRejectionReason.Profile);
      }
      if (!profileIds.Add(profile.ProfileId))
      {
        return new ConnectorSyncResult(
            ConnectorSyncStatus.Invalid,
            $"Profile '{profile.ProfileId}' appears more than once.",
            null,
            ConnectorSyncRejectionReason.DuplicateProfile);
      }
    }
    if (!IsValidProtocolProfileContracts(
        input.ProtocolVersion,
        input.Profiles))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "One or more manager contracts require a newer connector protocol version.",
          null,
          ConnectorSyncRejectionReason.ManagerContract);
    }
    if (input.ProtocolVersion < 3 &&
        (input.CapacityOperator is not null ||
         input.CapacityCommandOutcome is not null))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Capacity operation fields require connector protocol version 3.",
          null,
          ConnectorSyncRejectionReason.Capacity);
    }
    if (input.ProtocolVersion < 9 &&
        input.CapacityOperator?.Profiles.Any(profile =>
            profile.SupportsZeroMaximum) == true)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Zero-capacity support requires connector protocol version 9.",
          null,
          ConnectorSyncRejectionReason.Capacity);
    }
    if (!IsValidCapacityOperator(input.CapacityOperator) ||
        !IsValidCapacityOutcome(input.CapacityCommandOutcome))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Capacity operation state does not satisfy the protocol contract.",
          null,
          ConnectorSyncRejectionReason.Capacity);
    }
    if (input.ProtocolVersion < 4 &&
        (input.RecoveryOperator is not null ||
         input.RecoveryCommandProgress is not null ||
         input.RecoveryCommandOutcome is not null))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Manager recovery fields require connector protocol version 4.",
          null,
          ConnectorSyncRejectionReason.Recovery);
    }
    if (!IsValidRecoveryOperator(input.RecoveryOperator) ||
        !IsValidRecoveryProgress(input.RecoveryCommandProgress) ||
        !IsValidRecoveryOutcome(input.RecoveryCommandOutcome))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Manager recovery state does not satisfy the protocol contract.",
          null,
          ConnectorSyncRejectionReason.Recovery);
    }
    var acceptedAt = _timeProvider.GetUtcNow();
    if (input.ProfileInventory?.ObservedAt >
        acceptedAt.AddSeconds(
            _options.Value.HistoryClockSkewToleranceSeconds))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Profile inventory observation time exceeds the accepted clock-skew tolerance.",
          null,
          ConnectorSyncRejectionReason.InventoryClockSkew);
    }
    if (input.ProtocolVersion < 10 &&
        input.ConnectorHealth is not null)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Connector health replay requires connector protocol version 10.",
          null,
          ConnectorSyncRejectionReason.HealthReplay);
    }
    if (input.ProtocolVersion < PitCrewProtocol.ImageRolloutMinimumVersion &&
        (input.ImageRolloutOperator is not null ||
         input.ImageRolloutCommandProgress is not null ||
         input.ImageRolloutCommandOutcome is not null))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Profile-image rollout fields require connector protocol version 11.",
          null,
          ConnectorSyncRejectionReason.ImageRollout);
    }
    if (!IsValidImageRolloutOperator(input.ImageRolloutOperator) ||
        !IsValidImageRolloutProgress(input.ImageRolloutCommandProgress) ||
        !IsValidImageRolloutOutcome(input.ImageRolloutCommandOutcome))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Profile-image rollout state does not satisfy the protocol contract.",
          null,
          ConnectorSyncRejectionReason.ImageRollout);
    }
    if (!IsValidConnectorHealthReplay(
        input.ConnectorHealth,
        acceptedAt.AddSeconds(
            _options.Value.HistoryClockSkewToleranceSeconds)))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Connector health replay does not satisfy the protocol contract.",
          null,
          ConnectorSyncRejectionReason.HealthReplay);
    }

    var credentialUpdate = new ConnectorCredentialUpdate(
        ConnectorCredentialUpdateKind.None,
        string.Empty);
    ConnectorCredentialRotation? credentialRotation = null;
    if (identity.CredentialSlot == ConnectorCredentialSlot.Pending)
    {
      credentialUpdate = new ConnectorCredentialUpdate(
          ConnectorCredentialUpdateKind.Promote,
          _credentialService.Hash(credential));
    }
    else if (identity.RotationRequested &&
        input.ProtocolVersion >= 2)
    {
      var replacement = _credentialService.CreateNodeCredential();
      credentialUpdate = new ConnectorCredentialUpdate(
          ConnectorCredentialUpdateKind.Stage,
          _credentialService.Hash(replacement));
      credentialRotation = new ConnectorCredentialRotation(
          replacement);
    }

    await using var storageTransaction =
        await _transactionFactory.BeginAsync(cancellationToken);
    var historyPolicy =
        FleetHistoryPolicy.CreateAppendPolicy(_options.Value);
    var fleetApplyResult = await _fleetStore.ApplySyncAsync(
        storageTransaction,
        identity.NodeId,
        input.ConnectorVersion,
        acceptedAt,
        credentialUpdate,
        cancellationToken,
        input.ProfileInventory);
    IReadOnlySet<string> acceptedProfileIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (fleetApplyResult.ProfileInventoryAccepted)
    {
      acceptedProfileIds = await _fleetHistoryStore.AppendAsync(
          storageTransaction,
          identity.NodeId,
          input.Profiles,
          acceptedAt,
          historyPolicy,
          cancellationToken);
      await _fleetStore.ApplyProfilesAsync(
          storageTransaction,
          identity.NodeId,
          acceptedAt,
          input.Profiles,
          acceptedProfileIds,
          input.ProfileInventory,
          cancellationToken);
    }
    if (input.ConnectorHealth is not null)
    {
      await _connectorHealthStore.ApplyAsync(
          storageTransaction,
          identity.NodeId,
          input.ConnectorHealth,
          acceptedAt,
          new ConnectorHealthRetentionPolicy(
              TimeSpan.FromDays(
                  _options.Value.ConnectorHealthRetentionDays),
              _options.Value.MaximumConnectorHealthEventsPerNode),
          cancellationToken);
    }
    var completeProfileInventory =
        fleetApplyResult.ProfileInventoryAccepted &&
        (input.ProfileInventory is null ||
         input.ProfileInventory.Coverage == "complete");
    IReadOnlyList<ManagerObservedState>? hardwareProfiles =
        !completeProfileInventory
            ? null
            : input.Profiles.Count == 0
                ? []
                : acceptedProfileIds.Count == 0
                    ? null
                    : input.Profiles
                        .Where(profile =>
                            acceptedProfileIds.Contains(profile.ProfileId))
                        .ToArray();
    if (hardwareProfiles is not null)
    {
      await _fleetStore.ApplyHostHardwareAsync(
          storageTransaction,
          identity.NodeId,
          hardwareProfiles,
          input.Profiles
              .Select(profile => profile.ProfileId)
              .ToArray(),
          acceptedAt,
          cancellationToken);
      await _fleetHistoryStore.EnforceRetentionAsync(
          storageTransaction,
          identity.NodeId,
          acceptedAt,
          historyPolicy.Retention,
          cancellationToken);
    }
    await storageTransaction.CommitAsync(cancellationToken);
    SetCapacityCommand? capacityCommand = null;
    if (input.ProtocolVersion >= 3)
    {
      capacityCommand = await _capacityCommandStore.ApplyConnectorSyncAsync(
          identity.NodeId,
          input.CapacityOperator,
          input.CapacityCommandOutcome,
          acceptedAt,
          acceptedAt.Subtract(
              TimeSpan.FromSeconds(
                  _options.Value.CapacityCommandRedeliverySeconds)),
          cancellationToken,
          applyCapability: completeProfileInventory);
    }
    RecoverManagerCommand? recoveryCommand = null;
    if (input.ProtocolVersion >= 4)
    {
      recoveryCommand = await _recoveryCommandStore.ApplyConnectorSyncAsync(
          identity.NodeId,
          input.RecoveryOperator,
          input.RecoveryCommandProgress,
          input.RecoveryCommandOutcome,
          acceptedAt,
          acceptedAt.Subtract(
              TimeSpan.FromSeconds(
                  _options.Value.RecoveryCommandRedeliverySeconds)),
          cancellationToken,
          applyCapability: completeProfileInventory);
    }
    RollOutProfileImageCommand? imageRolloutCommand = null;
    if (input.ProtocolVersion >= PitCrewProtocol.ImageRolloutMinimumVersion)
    {
      imageRolloutCommand = await _imageRolloutCommandStore.ApplyConnectorSyncAsync(
          identity.NodeId,
          input.ImageRolloutOperator,
          input.ImageRolloutCommandProgress,
          input.ImageRolloutCommandOutcome,
          acceptedAt,
          acceptedAt.Subtract(
              TimeSpan.FromSeconds(
                  _options.Value.ImageRolloutCommandRedeliverySeconds)),
          cancellationToken,
          applyCapability: completeProfileInventory);
    }
    return new ConnectorSyncResult(
        ConnectorSyncStatus.Accepted,
        null,
        new ConnectorSyncResponse(
            acceptedAt,
            _options.Value.ConnectorPollSeconds,
            credentialRotation,
            capacityCommand,
            recoveryCommand,
            input.ConnectorHealth is null
                ? null
                : new ConnectorHealthAcknowledgement(
                    input.ConnectorHealth.Events
                        .Select(entry => entry.EventId)
                        .ToArray()),
            imageRolloutCommand));
  }

  internal static bool IsValidConnectorHealthReplay(
      ConnectorHealthReplay? replay,
      DateTimeOffset maximumTimestamp) =>
      ConnectorHealthReplayContract.IsValid(
          replay,
          maximumTimestamp);

  internal static bool IsValidCapacityOperator(
      CapacityOperatorCapability? capability)
  {
    if (capability is null)
    {
      return true;
    }
    if (capability.Profiles is null ||
        capability.Profiles.Count > 256)
    {
      return false;
    }

    var profileIds = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var profile in capability.Profiles)
    {
      if (!IsValidProfileId(profile.ProfileId) ||
          !profileIds.Add(profile.ProfileId) ||
          profile.Generation < 1 ||
          profile.CurrentMaximum < 0 ||
          (profile.CurrentMaximum == 0 &&
              !profile.SupportsZeroMaximum) ||
          profile.MaximumAllowed > 1_000_000 ||
          profile.MaximumAllowed < profile.CurrentMaximum)
      {
        return false;
      }
    }
    return true;
  }

  internal static bool IsValidCapacityOutcome(
      CapacityCommandOutcome? outcome)
  {
    if (outcome is null)
    {
      return true;
    }
    if (outcome.CommandId == Guid.Empty ||
        outcome.CompletedAt == default ||
        outcome.Message?.Length > 512 ||
        outcome.Status is not (
            "succeeded" or
            "rejected" or
            "failed"))
    {
      return false;
    }
    return outcome.Status == "succeeded"
        ? outcome.AcceptedGeneration is >= 1
        : outcome.AcceptedGeneration is null;
  }

  internal static bool IsValidRecoveryOperator(
      RecoveryOperatorCapability? capability)
  {
    if (capability is null)
    {
      return true;
    }
    if (capability.Profiles is null ||
        capability.Profiles.Count > 256)
    {
      return false;
    }

    var profileIds = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var profile in capability.Profiles)
    {
      if (!IsValidProfileId(profile.ProfileId) ||
          !profileIds.Add(profile.ProfileId) ||
          profile.ManagerContractVersion < 1 ||
          profile.DesiredGeneration < 0 ||
          profile.ObservedStateAgeSeconds < 0 ||
          profile.CommandTimeoutSeconds is < 1 or > 3600 ||
          profile.MaximumExpirySeconds is < 1 or > 86400 ||
          profile.ExpectedManagerInstanceId?.Length > 128 ||
          profile.DesiredStateHash is not null &&
          profile.DesiredStateHash.Length != 64)
      {
        return false;
      }
    }
    return true;
  }

  internal static bool IsValidRecoveryProgress(
      RecoveryCommandProgress? progress) =>
      progress is null ||
      progress.CommandId != Guid.Empty &&
      progress.ReportedAt != default &&
      progress.Phase is ("claimed" or "started");

  internal static bool IsValidRecoveryOutcome(
      RecoveryCommandOutcome? outcome)
  {
    if (outcome is null)
    {
      return true;
    }
    if (outcome.CommandId == Guid.Empty ||
        outcome.CompletedAt == default ||
        outcome.Message?.Length > 512 ||
        outcome.BeforeManagerInstanceId?.Length > 128 ||
        outcome.AfterManagerInstanceId?.Length > 128 ||
        outcome.Status is not (
            "succeeded" or
            "rejected" or
            "failed" or
            "indeterminate"))
    {
      return false;
    }
    return outcome.Status == "succeeded"
        ? outcome.FailureCategory is null &&
            !string.IsNullOrWhiteSpace(outcome.AfterManagerInstanceId)
        : outcome.FailureCategory is (
            "not-allowed" or
            "stale-fence" or
            "expired" or
            "manager-unresolved" or
            "operation-active" or
            "timeout" or
            "process-failure" or
            "unknown");
  }

  internal static bool IsValidRecoveryFences(
      string expectedManagerInstanceId,
      int expectedGeneration,
      string? expectedDesiredStateHash)
  {
    if (string.IsNullOrWhiteSpace(expectedManagerInstanceId) ||
        expectedManagerInstanceId.Length > 128 ||
        expectedGeneration < 0)
    {
      return false;
    }

    return expectedDesiredStateHash is null ||
        expectedDesiredStateHash.Length == 64 &&
        expectedDesiredStateHash.All(character =>
            character is >= '0' and <= '9' or
                >= 'a' and <= 'f' or
                >= 'A' and <= 'F');
  }

  internal static bool IsValidImageRolloutOperator(
      ImageRolloutOperatorCapability? capability)
  {
    if (capability is null)
    {
      return true;
    }
    if (capability.Profiles is null ||
        capability.Profiles.Count > 256)
    {
      return false;
    }
    var profileIds = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var profile in capability.Profiles)
    {
      if (!IsValidProfileId(profile.ProfileId) ||
          !profileIds.Add(profile.ProfileId) ||
          profile.Architecture is not
              ("linux/amd64" or "linux/arm64") ||
          !IsValidOptionalDigest(profile.CurrentImageDigest) ||
          !IsValidOptionalDigest(profile.CurrentLocalImageId) ||
          !IsValidOptionalHex(profile.CurrentWorkerRevision, 64) ||
          !IsValidHex(profile.StaticFingerprint, 64) ||
          !IsValidHex(profile.PreservedConfigurationFingerprint, 64) ||
          !IsValidHex(profile.RoutingFingerprint, 64) ||
          profile.DesiredGeneration < 0 ||
          !IsValidOptionalHex(profile.DesiredStateHash, 64) ||
          !IsValidOptionalImageReference(profile.CurrentImageReference) ||
          profile.AllowedRecipeIds is null ||
          profile.AllowedRecipeIds.Count > 64 ||
          profile.AllowedRecipeIds.Any(recipeId =>
              !IsValidRecipeId(recipeId)) ||
          profile.ObservedStateAgeSeconds is < 0 or > 86_400 ||
          profile.CommandTimeoutSeconds is < 1 or > 3600 ||
          profile.MaximumExpirySeconds is < 1 or > 86_400 ||
          profile.ManagerConvergenceStatus is not
              ("current" or "rolling" or "degraded") ||
          profile.CurrentWorkers is < 0 ||
          profile.StaleWorkers is < 0 ||
          profile.LocalFailureCategory is not null &&
          profile.LocalFailureCategory is not (
              "unsupported-architecture" or
              "unsupported-schema" or
              "unsupported-manager" or
              "unsupported-topology" or
              "not-allowed" or
              "policy-disabled" or
              "recipe-not-allowed" or
              "registry-not-allowed" or
              "stale-observed-state"))
      {
        return false;
      }
    }
    return true;
  }

  internal static bool IsValidImageRolloutProgress(
      ImageRolloutCommandProgress? progress) =>
      progress is null ||
      progress.CommandId != Guid.Empty &&
      progress.ReportedAt != default &&
      progress.Phase is ("claimed" or "started");

  internal static bool IsValidImageRolloutOutcome(
      ImageRolloutCommandOutcome? outcome)
  {
    if (outcome is null)
    {
      return true;
    }
    if (outcome.CommandId == Guid.Empty ||
        outcome.CompletedAt == default ||
        outcome.Message?.Length > 512 ||
        outcome.LastError?.Length > 128 ||
        outcome.Status is not (
            "succeeded" or
            "rejected" or
            "failed" or
            "indeterminate") ||
        outcome.ManagerConvergenceStatus is not
            ("current" or "rolling" or "degraded") ||
        outcome.CurrentWorkers is < 0 ||
        outcome.StaleWorkers is < 0 ||
        !IsValidOptionalDigest(outcome.TargetDigest) ||
        !IsValidOptionalHex(outcome.TargetWorkerRevision, 64))
    {
      return false;
    }
    return outcome.Status == "succeeded"
        ? outcome.FailureCategory is null &&
            outcome.TargetDigest is not null &&
            outcome.TargetWorkerRevision is not null &&
            outcome.CurrentWorkers is not null &&
            outcome.StaleWorkers is not null
        : outcome.FailureCategory is (
            "not-allowed" or
            "recipe-not-allowed" or
            "registry-not-allowed" or
            "stale-fence" or
            "expired" or
            "unsupported" or
            "unsupported-architecture" or
            "unsupported-topology" or
            "operation-active" or
            "timeout" or
            "process-failure" or
            "unknown");
  }

  internal static bool IsValidImageRolloutFences(
      ImageRolloutCommandFences fences)
  {
    if (fences is null ||
        !IsValidHex(fences.ExpectedStaticFingerprint, 64) ||
        !IsValidHex(fences.ExpectedPreservedConfigurationFingerprint, 64) ||
        !IsValidHex(fences.ExpectedRoutingFingerprint, 64) ||
        fences.ExpectedDesiredGeneration < 0 ||
        !IsValidOptionalHex(fences.ExpectedDesiredStateHash, 64) ||
        !IsValidOptionalHex(fences.ExpectedCurrentWorkerRevision, 64) ||
        !IsValidOptionalDigest(fences.ExpectedCurrentImageDigest) ||
        !IsValidOptionalDigest(fences.ExpectedCurrentLocalImageId) ||
        !IsValidOptionalImageReference(fences.ExpectedCurrentImageReference))
    {
      return false;
    }
    return true;
  }

  internal static bool IsValidImageRolloutTargetDigest(string digest) =>
      digest is { Length: 71 } &&
      digest.StartsWith("sha256:", StringComparison.Ordinal) &&
      IsAllLowerHex(digest.AsSpan(7));

  internal static bool IsValidImageRolloutTargetPlatform(string platform) =>
      platform is "linux/amd64" or "linux/arm64";

  internal static bool IsValidRecipeId(string recipeId) =>
      !string.IsNullOrWhiteSpace(recipeId) &&
      recipeId.Length is >= 1 and <= 100 &&
      recipeId.All(character =>
          character is
              >= 'a' and <= 'z' or
              >= 'A' and <= 'Z' or
              >= '0' and <= '9' or
              '-' or '_');

  internal static bool IsValidProfile(ManagerObservedState profile) =>
      ManagerObservedStateValidator.IsValid(profile);

  internal static bool IsValidProfileId(string profileId) =>
      PitCrewProfileId.IsValid(profileId);

  internal static bool IsValidProtocolProfileContracts(
      int protocolVersion,
      IReadOnlyList<ManagerObservedState> profiles) =>
      ManagerObservedStateValidator.IsValidProtocolProfileContracts(
          protocolVersion, profiles);

  internal static bool IsValidProfileInventory(
      int protocolVersion,
      ConnectorProfileInventory? inventory,
      int profileCount) =>
      ManagerObservedStateValidator.IsValidProfileInventory(
          protocolVersion, inventory, profileCount);
}
