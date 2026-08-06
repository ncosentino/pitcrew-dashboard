using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
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
    if (!IsValidProtocolProfileContracts(
        input.ProtocolVersion,
        input.Profiles))
    {
      return new ConnectorSyncResult(
          ConnectorSyncStatus.Invalid,
          "Manager contract 14 requires connector protocol version 7.",
          null);
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
    var historyPolicy =
        FleetHistoryPolicy.CreateAppendPolicy(_options.Value);
    var acceptedProfileIds = await _fleetHistoryStore.AppendAsync(
        storageTransaction,
        identity.NodeId,
        input.Profiles,
        acceptedAt,
        historyPolicy,
        cancellationToken);
    await _fleetStore.ApplySyncAsync(
        storageTransaction,
        identity.NodeId,
        input.ConnectorVersion,
        acceptedAt,
        input.Profiles,
        acceptedProfileIds,
        credentialUpdate,
        cancellationToken);
    IReadOnlyList<ManagerObservedState>? hardwareProfiles =
        input.Profiles.Count == 0
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
        !IsValidWorkerUpdate(profile) ||
        !IsValidHostHardware(profile) ||
        !IsValidAutoscaling(profile) ||
        !IsValidResourceTelemetry(profile) ||
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
    var runnerNameHashes = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var slot in profile.Slots)
    {
      if (string.IsNullOrWhiteSpace(slot.Key) ||
          slot.Key.Length > 128 ||
          !slotKeys.Add(slot.Key) ||
          slot.Repository is not null &&
          (string.IsNullOrWhiteSpace(slot.Repository) ||
           slot.Repository.Length > 2048) ||
          slot.Target is not null &&
          (string.IsNullOrWhiteSpace(slot.Target) ||
           slot.Target.Length > 512) ||
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
          !IsValidCurrentJob(profile, slot) ||
          slot.RunnerNameHash is not null &&
          (!Regex.IsMatch(
              slot.RunnerNameHash,
              "^[0-9a-f]{64}$",
              RegexOptions.CultureInvariant) ||
           !runnerNameHashes.Add(slot.RunnerNameHash)) ||
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
    if (profile.ManagerContractVersion < 14 &&
        profile.Slots.Any(slot =>
            slot.RunnerNameHash is not null))
    {
      return false;
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

  internal static bool IsValidProtocolProfileContracts(
      int protocolVersion,
      IReadOnlyList<ManagerObservedState> profiles) =>
      profiles.All(profile =>
          profile.ManagerContractVersion switch
          {
            >= 15 => protocolVersion >= 8,
            >= 14 => protocolVersion >= 7,
            _ => true,
          });

  private static bool IsValidHostHardware(
      ManagerObservedState profile)
  {
    var hardware = profile.Host?.Hardware;
    if (profile.ManagerContractVersion < 13)
    {
      return hardware is null;
    }
    if (profile.ManagerContractVersion >= 13 && hardware is null)
    {
      return false;
    }
    if (hardware is null)
    {
      return true;
    }
    if (hardware.Status is not (
            "current" or
            "stale" or
            "unavailable") ||
        hardware.AttemptedAt == default ||
        hardware.AttemptedAt > profile.ObservedAt ||
        !IsHardwareText(hardware.ProcessorModel, 256) ||
        !IsHardwareText(hardware.Architecture, 64) ||
        !IsPositiveOrNull(hardware.PhysicalCoreCount) ||
        !IsPositiveOrNull(hardware.LogicalProcessorCount) ||
        !IsPositiveOrNull(hardware.PerformanceCoreCount) ||
        !IsPositiveOrNull(hardware.EfficiencyCoreCount) ||
        !IsPositiveOrNull(hardware.MemoryBytes) ||
        !IsHardwareText(hardware.OperatingSystem, 256) ||
        !IsHardwareText(hardware.KernelVersion, 256) ||
        !IsHardwareText(hardware.DockerServerVersion, 256) ||
        !IsHardwareText(hardware.DockerStorageDriver, 256) ||
        !IsHardwareText(hardware.DockerBackingFilesystem, 256))
    {
      return false;
    }
    var hasValue = HardwareValues(hardware).Any(value => value);
    if (hardware.Status == "unavailable")
    {
      return hardware.CollectedAt is null &&
          hardware.InventoryHash is null &&
          !hasValue;
    }
    if (hardware.CollectedAt is null ||
        hardware.CollectedAt > hardware.AttemptedAt ||
        hardware.InventoryHash is null ||
        !Regex.IsMatch(
            hardware.InventoryHash,
            "^[0-9a-f]{64}$",
            RegexOptions.CultureInvariant) ||
        !hasValue)
    {
      return false;
    }
    return string.Equals(
        hardware.InventoryHash,
        ComputeHardwareHash(hardware),
        StringComparison.Ordinal);
  }

  private static IEnumerable<bool> HardwareValues(
      HostHardwareInventory hardware)
  {
    yield return hardware.ProcessorModel is not null;
    yield return hardware.Architecture is not null;
    yield return hardware.PhysicalCoreCount is not null;
    yield return hardware.LogicalProcessorCount is not null;
    yield return hardware.PerformanceCoreCount is not null;
    yield return hardware.EfficiencyCoreCount is not null;
    yield return hardware.MemoryBytes is not null;
    yield return hardware.OperatingSystem is not null;
    yield return hardware.KernelVersion is not null;
    yield return hardware.DockerServerVersion is not null;
    yield return hardware.DockerStorageDriver is not null;
    yield return hardware.DockerBackingFilesystem is not null;
  }

  private static bool IsHardwareText(
      string? value,
      int maximumLength) =>
      value is null ||
      value.Length is >= 1 &&
      value.Length <= maximumLength &&
      !value.Any(char.IsControl);

  private static bool IsPositiveOrNull(long? value) =>
      value is null or > 0;

  private static string ComputeHardwareHash(
      HostHardwareInventory hardware)
  {
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(
        stream,
        new JsonWriterOptions
        {
          Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
    {
      writer.WriteStartObject();
      WriteHardwareString(
          writer,
          "processorModel",
          hardware.ProcessorModel);
      WriteHardwareString(
          writer,
          "architecture",
          hardware.Architecture);
      WriteHardwareNumber(
          writer,
          "physicalCoreCount",
          hardware.PhysicalCoreCount);
      WriteHardwareNumber(
          writer,
          "logicalProcessorCount",
          hardware.LogicalProcessorCount);
      WriteHardwareNumber(
          writer,
          "performanceCoreCount",
          hardware.PerformanceCoreCount);
      WriteHardwareNumber(
          writer,
          "efficiencyCoreCount",
          hardware.EfficiencyCoreCount);
      WriteHardwareNumber(
          writer,
          "memoryBytes",
          hardware.MemoryBytes);
      WriteHardwareString(
          writer,
          "operatingSystem",
          hardware.OperatingSystem);
      WriteHardwareString(
          writer,
          "kernelVersion",
          hardware.KernelVersion);
      WriteHardwareString(
          writer,
          "dockerServerVersion",
          hardware.DockerServerVersion);
      WriteHardwareString(
          writer,
          "dockerStorageDriver",
          hardware.DockerStorageDriver);
      WriteHardwareString(
          writer,
          "dockerBackingFilesystem",
          hardware.DockerBackingFilesystem);
      writer.WriteEndObject();
    }
    return Convert.ToHexString(
        SHA256.HashData(stream.ToArray()))
        .ToLowerInvariant();
  }

  private static void WriteHardwareString(
      Utf8JsonWriter writer,
      string propertyName,
      string? value)
  {
    if (value is null)
    {
      writer.WriteNull(propertyName);
    }
    else
    {
      writer.WriteString(propertyName, value);
    }
  }

  private static void WriteHardwareNumber(
      Utf8JsonWriter writer,
      string propertyName,
      long? value)
  {
    if (value is null)
    {
      writer.WriteNull(propertyName);
    }
    else
    {
      writer.WriteNumber(propertyName, value.Value);
    }
  }

  private static bool IsValidWorkerUpdate(ManagerObservedState profile)
  {
    var update = profile.Update;
    if (update is null)
    {
      return true;
    }

    if (update.Status is not ("current" or "rolling" or "degraded") ||
        string.IsNullOrWhiteSpace(update.TargetRevision) ||
        update.TargetRevision.Length != 64 ||
        !update.TargetRevision.All(character =>
            character is >= '0' and <= '9' or
                >= 'a' and <= 'f') ||
        update.CurrentWorkers < 0 ||
        update.StaleWorkers < 0 ||
        update.CurrentWorkers + update.StaleWorkers != profile.ActiveSlots ||
        update.Status == "current" && update.StaleWorkers != 0 ||
        update.Status == "rolling" && update.StaleWorkers == 0 ||
        update.TargetImage is not null &&
        (update.TargetImage.Length is < 1 or > 2048 ||
         update.TargetImage.Any(char.IsWhiteSpace)) ||
        !IsValidImageId(update.TargetImageId) ||
        update.TargetImageId is not null && update.TargetImage is null ||
        update.LastError?.Length > 512)
    {
      return false;
    }

    return true;
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
      ManagerObservedState profile)
  {
    var telemetry = profile.ResourceTelemetry;
    if (telemetry is null)
    {
      return profile.ManagerContractVersion < 16;
    }

    if (telemetry.SampledAt == default ||
        telemetry.SampledAt > profile.ObservedAt ||
        telemetry.Status is not (
            "available" or
            "partial" or
            "unavailable") ||
        !IsValidHostCapacity(telemetry.Host) ||
        !IsValidResourceUsage(telemetry.Manager))
    {
      return false;
    }

    return profile.ManagerContractVersion switch
    {
      < 16 => telemetry.HostPressure is null,
      _ => IsValidHostPressure(telemetry.HostPressure),
    };
  }

  private static bool IsValidCurrentJob(
      ManagerObservedState profile,
      ObservedSlotState slot)
  {
    var job = slot.CurrentJob;
    if (profile.ManagerContractVersion < 15)
    {
      return job is null;
    }
    if (job is null)
    {
      return true;
    }
    if (!slot.ProcessRunning ||
        slot.RunnerNameHash is null ||
        slot.Activity is not ("busy" or "draining") ||
        !GitHubRepositoryPattern().IsMatch(job.Repository) ||
        job.WorkflowRunId <= 0 ||
        !JobIdPattern().IsMatch(job.JobId) ||
        !IsBoundedText(job.DisplayName, 256) ||
        !IsBoundedText(job.EventName, 64) ||
        !IsBoundedText(job.Result, 64) ||
        job.Result is not null && job.FinishedAt is null ||
        job.StartedAt == default ||
        job.StartedAt > profile.ObservedAt ||
        job.FinishedAt is { } finishedAt &&
        (finishedAt < job.StartedAt ||
         finishedAt > profile.ObservedAt))
    {
      return false;
    }

    DateTimeOffset? previous = null;
    foreach (var timestamp in new[]
    {
        job.QueuedAt,
        job.ScaleSetAssignedAt,
        job.RunnerAssignedAt,
        (DateTimeOffset?)job.StartedAt,
        job.FinishedAt,
    })
    {
      if (timestamp is null)
      {
        continue;
      }
      if (previous is not null && timestamp < previous)
      {
        return false;
      }
      previous = timestamp;
    }
    return true;
  }

  private static bool IsValidHostPressure(
      HostPressureTelemetry? pressure)
  {
    if (pressure is null ||
        pressure.Source is not "docker-host" ||
        pressure.Status is not (
            "available" or
            "partial" or
            "unavailable") ||
        !IsPercentageOrNull(pressure.CpuUtilizationPercent) ||
        !IsNonnegativeFiniteOrNull(pressure.Load1) ||
        !IsNonnegativeFiniteOrNull(pressure.Load5) ||
        !IsNonnegativeFiniteOrNull(pressure.Load15) ||
        pressure.MemoryTotalBytes is < 1 ||
        pressure.MemoryAvailableBytes is < 0 ||
        pressure.SwapUsedBytes is < 0 ||
        pressure.MemoryTotalBytes is not null &&
        pressure.MemoryAvailableBytes > pressure.MemoryTotalBytes ||
        !IsPercentageOrNull(pressure.CpuPressureSomeAvg10) ||
        !IsPercentageOrNull(pressure.CpuPressureFullAvg10) ||
        !IsPercentageOrNull(pressure.MemoryPressureSomeAvg10) ||
        !IsPercentageOrNull(pressure.MemoryPressureFullAvg10) ||
        !IsPercentageOrNull(pressure.IoPressureSomeAvg10) ||
        !IsPercentageOrNull(pressure.IoPressureFullAvg10))
    {
      return false;
    }

    var measurements = new object?[]
    {
        pressure.CpuUtilizationPercent,
        pressure.Load1,
        pressure.Load5,
        pressure.Load15,
        pressure.MemoryTotalBytes,
        pressure.MemoryAvailableBytes,
        pressure.SwapUsedBytes,
        pressure.CpuPressureSomeAvg10,
        pressure.CpuPressureFullAvg10,
        pressure.MemoryPressureSomeAvg10,
        pressure.MemoryPressureFullAvg10,
        pressure.IoPressureSomeAvg10,
        pressure.IoPressureFullAvg10,
    };
    var coreAvailable =
        pressure.CpuUtilizationPercent is not null &&
        pressure.Load1 is not null &&
        pressure.Load5 is not null &&
        pressure.Load15 is not null &&
        pressure.MemoryTotalBytes is not null &&
        pressure.MemoryAvailableBytes is not null &&
        pressure.SwapUsedBytes is not null;
    return pressure.Status switch
    {
      "available" => coreAvailable,
      "partial" => !coreAvailable &&
          measurements.Any(measurement => measurement is not null),
      "unavailable" => measurements.All(measurement => measurement is null),
      _ => false,
    };
  }

  private static bool IsBoundedText(string? value, int maximumLength) =>
      value is null ||
      value.Length is >= 1 &&
      value.Length <= maximumLength &&
      !value.Any(char.IsControl);

  private static bool IsNonnegativeFiniteOrNull(double? value) =>
      value is null ||
      double.IsFinite(value.Value) &&
      value.Value >= 0;

  private static bool IsPercentageOrNull(double? value) =>
      IsNonnegativeFiniteOrNull(value) &&
      value is not > 100;

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
      => PitCrewProfileId.IsValid(profileId);

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

  [GeneratedRegex(
      @"^https://github\.com/[A-Za-z0-9._-]{1,39}/[A-Za-z0-9._-]{1,100}$",
      RegexOptions.CultureInvariant,
      matchTimeoutMilliseconds: 100)]
  private static partial Regex GitHubRepositoryPattern();

  [GeneratedRegex(
      @"^[1-9][0-9]{0,31}$",
      RegexOptions.CultureInvariant,
      matchTimeoutMilliseconds: 100)]
  private static partial Regex JobIdPattern();
}
