using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

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
    ConnectorSyncResponse? Response);

internal sealed record ConnectorSynchronizationInput(
    int ProtocolVersion,
    string ConnectorVersion,
    DateTimeOffset SentAt,
    IReadOnlyList<ManagerObservedState> Profiles,
    CapacityOperatorCapability? CapacityOperator,
    CapacityCommandOutcome? CapacityCommandOutcome,
    RecoveryOperatorCapability? RecoveryOperator,
    RecoveryCommandProgress? RecoveryCommandProgress,
    RecoveryCommandOutcome? RecoveryCommandOutcome);

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
    IFleetStorageTransactionFactory _transactionFactory,
    ICapacityCommandStore _capacityCommandStore,
    IRecoveryCommandStore _recoveryCommandStore,
    ConnectorCredentialService _credentialService,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : ISyncConnectorUnitOfWork
{
  private const long MinimumWorkerMemoryBytes = 6_291_456;

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
          null);
    }
    if (string.IsNullOrWhiteSpace(input.ConnectorVersion) ||
        input.ConnectorVersion.Length > 128)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Connector version must be between 1 and 128 characters.",
          null);
    }
    if (input.Profiles is null ||
        input.Profiles.Count > 256)
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "A connector cannot synchronize more than 256 profiles.",
          null);
    }

    var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var profile in input.Profiles)
    {
      if (!IsValidProfile(profile))
      {
        return new ConnectorSyncResult(
            ConnectorSyncStatus.Invalid,
            $"Profile '{profile.ProfileId}' does not satisfy the observed-state contract.",
            null);
      }
      if (!profileIds.Add(profile.ProfileId))
      {
        return new ConnectorSyncResult(
            ConnectorSyncStatus.Invalid,
            $"Profile '{profile.ProfileId}' appears more than once.",
            null);
      }
    }
    if (input.ProtocolVersion < 3 &&
        (input.CapacityOperator is not null ||
         input.CapacityCommandOutcome is not null))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Capacity operation fields require connector protocol version 3.",
          null);
    }
    if (!IsValidCapacityOperator(input.CapacityOperator) ||
        !IsValidCapacityOutcome(input.CapacityCommandOutcome))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Capacity operation state does not satisfy the protocol contract.",
          null);
    }
    if (input.ProtocolVersion < 4 &&
        (input.RecoveryOperator is not null ||
         input.RecoveryCommandProgress is not null ||
         input.RecoveryCommandOutcome is not null))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Manager recovery fields require connector protocol version 4.",
          null);
    }
    if (!IsValidRecoveryOperator(input.RecoveryOperator) ||
        !IsValidRecoveryProgress(input.RecoveryCommandProgress) ||
        !IsValidRecoveryOutcome(input.RecoveryCommandOutcome))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Manager recovery state does not satisfy the protocol contract.",
          null);
    }

    var acceptedAt = _timeProvider.GetUtcNow();
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
    await _fleetStore.ApplySyncAsync(
        storageTransaction,
        identity.NodeId,
        input.ConnectorVersion,
        acceptedAt,
        input.Profiles,
        credentialUpdate,
        cancellationToken);
    await _fleetHistoryStore.AppendAsync(
        storageTransaction,
        identity.NodeId,
        input.Profiles,
        acceptedAt,
        FleetHistoryPolicy.CreateAppendPolicy(_options.Value),
        cancellationToken);
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
          cancellationToken);
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
          cancellationToken);
    }
    return new ConnectorSyncResult(
        ConnectorSyncStatus.Accepted,
        null,
        new ConnectorSyncResponse(
            acceptedAt,
            _options.Value.ConnectorPollSeconds,
            credentialRotation,
            capacityCommand,
            recoveryCommand));
  }

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
          profile.CurrentMaximum < 1 ||
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

  internal static bool IsValidProfile(ManagerObservedState profile)
  {
    if (profile.SchemaVersion != 1 ||
        profile.ManagerContractVersion < 5 ||
        string.IsNullOrWhiteSpace(profile.ProfileId) ||
        !IsValidProfileId(profile.ProfileId) ||
        string.IsNullOrWhiteSpace(profile.ManagerInstanceId) ||
        profile.ManagerInstanceId.Length > 128 ||
        profile.ObservedAt == default ||
        profile.Generation < 0 ||
        profile.DesiredSlots < 0 ||
        profile.ActiveSlots < 0 ||
        profile.DrainingSlots < 0 ||
        profile.EligibleSlots is < 0 ||
        profile.Slots is null ||
        profile.Slots.Count > 10000 ||
        profile.ConfiguredSlots is < 0 ||
        !IsValidResourcePolicy(profile.ResourcePolicy) ||
        !IsValidAutoscaling(profile) ||
        !IsValidResourceTelemetry(profile.ResourceTelemetry) ||
        profile.ActiveSlots != profile.Slots.Count(slot => slot.ProcessRunning) ||
        profile.DrainingSlots != profile.Slots.Count(slot =>
            string.Equals(
                slot.State,
                "draining",
                StringComparison.OrdinalIgnoreCase)) ||
        profile.ManagerStatus is not (
            "starting" or
            "running" or
            "stopping" or
            "stopped") ||
        profile.Scope is not ("repo" or "org" or "ent") ||
        profile.DesiredStateStatus is not (
            "waiting" or
            "accepted" or
            "invalid" or
            "stale" or
            "conflict") ||
        profile.DesiredStateHash is not null &&
        profile.DesiredStateHash.Length != 64)
    {
      return false;
    }

    var slotKeys = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var slot in profile.Slots)
    {
      if (string.IsNullOrWhiteSpace(slot.Key) ||
          slot.Key.Length > 128 ||
          !slotKeys.Add(slot.Key) ||
          slot.Repository?.Length > 2048 ||
          slot.FailureCount < 0 ||
          slot.BackoffSeconds < 0 ||
          slot.Activity is not null &&
          slot.Activity is not (
              "starting" or
              "idle" or
              "busy" or
              "draining" or
              "unknown") ||
          slot.RegistrationStatus is not null &&
          slot.RegistrationStatus is not (
              "connected" or
              "disconnected" or
              "registration-missing" or
              "unknown") ||
          !IsValidResourceUsage(slot.Resources) ||
          !IsValidImageId(slot.ImageId) ||
          !IsValidLastExit(slot.LastExit) ||
          slot.State is not (
              "starting" or
              "online" or
              "backoff" or
              "restarting" or
              "draining" or
              "stopped"))
      {
        return false;
      }
    }

    if (profile.ManagerContractVersion >= 10 &&
        (profile.EligibleSlots is null ||
         profile.Slots.Any(slot => slot.RegistrationStatus is null)))
    {
      return false;
    }
    if (profile.EligibleSlots is not null &&
        profile.EligibleSlots != profile.Slots.Count(slot =>
            string.Equals(
                slot.RegistrationStatus,
                "connected",
                StringComparison.Ordinal)))
    {
      return false;
    }

    return IsConsistentResourceTelemetry(profile) &&
        ManagerDiagnosticsValidator.IsValid(profile);
  }

  private static bool IsValidAutoscaling(ManagerObservedState profile)
  {
    var autoscaling = profile.Autoscaling;
    if (autoscaling is null)
    {
      return true;
    }

    if (autoscaling.Mode is not "scale-set" ||
        autoscaling.Status is not (
            "starting" or
            "running" or
            "degraded" or
            "stopping") ||
        autoscaling.MinimumIdleSlots < 0 ||
        autoscaling.MaximumSlots < 0 ||
        autoscaling.TargetSlots < 0 ||
        autoscaling.AssignedJobs < 0 ||
        autoscaling.RunningJobs < 0 ||
        autoscaling.AvailableJobs < 0 ||
        autoscaling.IdleRunners < 0 ||
        autoscaling.BusyRunners < 0 ||
        autoscaling.ScaleDownDelaySeconds < 0 ||
        autoscaling.ScaleSetCount < 0 ||
        (autoscaling.ScaleDownAt is { } scaleDownAt &&
         scaleDownAt == default))
    {
      return false;
    }

    return (profile.ConfiguredSlots is null ||
            autoscaling.MaximumSlots == profile.ConfiguredSlots) &&
        profile.DesiredSlots == autoscaling.TargetSlots &&
        autoscaling.TargetSlots <= autoscaling.MaximumSlots &&
        autoscaling.RunningJobs <= autoscaling.AssignedJobs &&
        autoscaling.BusyRunners <= profile.ActiveSlots &&
        autoscaling.IdleRunners <=
            profile.ActiveSlots - autoscaling.BusyRunners &&
        IsValidAutoscalingTargets(profile, autoscaling);
  }

  private static bool IsValidAutoscalingTargets(
      ManagerObservedState profile,
      ManagerAutoscalingState autoscaling)
  {
    if (profile.ManagerContractVersion >= 11 &&
        (autoscaling.MaximumActiveWorkers is null ||
         autoscaling.Targets is null))
    {
      return false;
    }
    if (autoscaling.MaximumActiveWorkers is < 0)
    {
      return false;
    }

    var targets = autoscaling.Targets;
    if (targets is null)
    {
      return true;
    }
    if (targets.Count > 10000)
    {
      return false;
    }

    var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var localActiveWorkers = 0;
    var localIdleWorkers = 0;
    var localBusyWorkers = 0;
    var targetSlots = 0;
    foreach (var target in targets)
    {
      if (string.IsNullOrWhiteSpace(target.Key) ||
          target.Key.Length > 512 ||
          !targetKeys.Add(target.Key) ||
          target.Repository?.Length > 2048 ||
          target.MaximumSlots < 0 ||
          target.TargetSlots < 0 ||
          target.LocalActiveWorkers < 0 ||
          target.LocalIdleWorkers < 0 ||
          target.LocalBusyWorkers < 0 ||
          target.LocalDrainingWorkers < 0 ||
          !IsValidScaleSetStatistics(target.Statistics))
      {
        return false;
      }

      localActiveWorkers += target.LocalActiveWorkers;
      localIdleWorkers += target.LocalIdleWorkers;
      localBusyWorkers += target.LocalBusyWorkers;
      targetSlots += target.TargetSlots;
    }

    return localActiveWorkers <= profile.ActiveSlots &&
        localIdleWorkers == autoscaling.IdleRunners &&
        localBusyWorkers == autoscaling.BusyRunners &&
        targetSlots == autoscaling.TargetSlots;
  }

  private static bool IsValidScaleSetStatistics(
      ScaleSetStatistics? statistics) =>
      statistics is null ||
      statistics.ObservedAt != default &&
      statistics.AvailableJobs >= 0 &&
      statistics.AcquiredJobs >= 0 &&
      statistics.AssignedJobs >= 0 &&
      statistics.RunningJobs >= 0 &&
      statistics.RegisteredRunners >= 0 &&
      statistics.BusyRunners >= 0 &&
      statistics.IdleRunners >= 0 &&
      statistics.RunningJobs <= statistics.AssignedJobs;

  private static bool IsValidResourceTelemetry(
      ManagerResourceTelemetry? telemetry) =>
      telemetry is null ||
      telemetry.SampledAt != default &&
      telemetry.Status is (
          "available" or
          "partial" or
          "unavailable") &&
      IsValidHostCapacity(telemetry.Host) &&
      IsValidResourceUsage(telemetry.Manager);

  private static bool IsValidHostCapacity(
      HostResourceCapacity? host) =>
      host is null ||
      host.LogicalProcessorCount > 0 &&
      host.MemoryBytes > 0;

  private static bool IsValidResourceUsage(ResourceUsage? resources) =>
      resources is null ||
      double.IsFinite(resources.CpuCores) &&
      resources.CpuCores >= 0 &&
      resources.MemoryWorkingSetBytes >= 0 &&
      resources.Pids >= 0 &&
      resources.NetworkRxBytes is not < 0 &&
      resources.NetworkTxBytes is not < 0 &&
      resources.BlockReadBytes is not < 0 &&
      resources.BlockWriteBytes is not < 0;

  private static bool IsValidResourcePolicy(WorkerResourcePolicy? policy)
  {
    if (policy is null)
    {
      return true;
    }
    if (policy.MemoryBytes is not null &&
        policy.MemoryBytes < MinimumWorkerMemoryBytes)
    {
      return false;
    }
    if (policy.MemorySwapBytes is not null &&
        (policy.MemorySwapBytes < MinimumWorkerMemoryBytes ||
         policy.MemoryBytes is null ||
         policy.MemorySwapBytes < policy.MemoryBytes))
    {
      return false;
    }
    if (policy.CpuCores is not null &&
        (policy.CpuCores.Length > 32 ||
         !WorkerCpuCoresPattern().IsMatch(policy.CpuCores)))
    {
      return false;
    }
    if (policy.Pids is not null &&
        policy.Pids < 1)
    {
      return false;
    }

    return policy.MemoryBytes is not null ||
        policy.MemorySwapBytes is not null ||
        policy.CpuCores is not null ||
        policy.Pids is not null;
  }

  private static bool IsValidImageId(string? imageId) =>
      imageId is null ||
      imageId.Length == 71 &&
      WorkerImageIdPattern().IsMatch(imageId);

  private static bool IsValidLastExit(WorkerLastExitDiagnostic? lastExit)
  {
    if (lastExit is null)
    {
      return true;
    }
    if (lastExit.ObservedAt == default ||
        lastExit.Classification is not (
            "clean" or
            "oom-killed" or
            "sigkill" or
            "signal" or
            "error" or
            "launch-failure" or
            "unknown") ||
        lastExit.Evidence is not (
            "docker-inspect" or
            "docker-wait" or
            "launch" or
            "unavailable") ||
        lastExit.ExitCode is < 0 or > 255 ||
        lastExit.Signal is < 1 or > 64)
    {
      return false;
    }
    if (lastExit.Signal is { } signal &&
        lastExit.ExitCode != 128 + signal)
    {
      return false;
    }
    if (lastExit.DockerOomKilled is true &&
        lastExit.Classification is not "oom-killed")
    {
      return false;
    }

    return lastExit.Classification switch
    {
      "oom-killed" => lastExit.DockerOomKilled is true,
      "sigkill" => lastExit.Signal is 9,
      "signal" => lastExit.Signal is not null and not 9,
      "clean" => lastExit.ExitCode is 0 && lastExit.Signal is null,
      "error" => lastExit.ExitCode is not null and not 0 &&
          lastExit.Signal is null,
      "launch-failure" => lastExit.Evidence is "launch" &&
          lastExit.ExitCode is null &&
          lastExit.Signal is null,
      _ => lastExit.ExitCode is null && lastExit.Signal is null,
    };
  }

  private static bool IsConsistentResourceTelemetry(
      ManagerObservedState profile)
  {
    var telemetry = profile.ResourceTelemetry;
    var hasSlotResources = profile.Slots.Any(
        slot => slot.Resources is not null);
    if (telemetry is null)
    {
      return !hasSlotResources;
    }

    return telemetry.Status switch
    {
      "available" =>
          telemetry.Host is not null &&
          telemetry.Manager is not null,
      "partial" =>
          telemetry.Host is not null ||
          telemetry.Manager is not null ||
          hasSlotResources,
      "unavailable" =>
          telemetry.Host is null &&
          telemetry.Manager is null &&
          !hasSlotResources,
      _ => false,
    };
  }

  internal static bool IsValidProfileId(string profileId)
  {
    if (profileId.Length is < 1 or > 32 ||
        profileId[0] is < 'a' or > 'z')
    {
      return false;
    }

    return profileId.All(character =>
        character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-');
  }

  [GeneratedRegex(
      @"^sha256:[0-9a-f]{64}$",
      RegexOptions.CultureInvariant,
      matchTimeoutMilliseconds: 100)]
  private static partial Regex WorkerImageIdPattern();

  [GeneratedRegex(
      @"^(?:[1-9][0-9]*(?:\.[0-9]{1,9})?|0\.[0-9]{0,8}[1-9])$",
      RegexOptions.CultureInvariant,
      matchTimeoutMilliseconds: 100)]
  private static partial Regex WorkerCpuCoresPattern();
}
