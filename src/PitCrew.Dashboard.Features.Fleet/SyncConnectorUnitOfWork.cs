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
    CapacityCommandOutcome? CapacityCommandOutcome);

internal interface ISyncConnectorUnitOfWork
{
  Task<ConnectorSyncResult> SynchronizeAsync(
      string credential,
      ConnectorSynchronizationInput input,
      CancellationToken cancellationToken);
}

internal sealed class SyncConnectorUnitOfWork(
    IFleetStore _fleetStore,
    ICapacityCommandStore _capacityCommandStore,
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

    await _fleetStore.ApplySyncAsync(
        identity.NodeId,
        input.ConnectorVersion,
        acceptedAt,
        input.Profiles,
        credentialUpdate,
        cancellationToken);
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
    return new ConnectorSyncResult(
        ConnectorSyncStatus.Accepted,
        null,
        new ConnectorSyncResponse(
            acceptedAt,
            _options.Value.ConnectorPollSeconds,
            credentialRotation,
            capacityCommand));
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

    return IsConsistentResourceTelemetry(profile);
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
            profile.ActiveSlots - autoscaling.BusyRunners;
  }

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
      resources.Pids >= 0;

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
}
