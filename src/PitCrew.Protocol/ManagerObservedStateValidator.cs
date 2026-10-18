using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PitCrew.Protocol;

/// <summary>Shared semantic validation for connector and Dashboard observations.</summary>
public static partial class ManagerObservedStateValidator
{
  private const long MinimumWorkerMemoryBytes = 6_291_456;

  internal static bool IsValidHex(string value, int length)
      => value is not null &&
         value.Length == length &&
         IsAllLowerHex(value.AsSpan());

  internal static bool IsValidOptionalHex(string? value, int length)
      => value is null || IsValidHex(value, length);

  internal static bool IsValidOptionalDigest(string? value)
  {
    if (value is null)
    {
      return true;
    }
    return value is { Length: 71 } &&
           value.StartsWith("sha256:", StringComparison.Ordinal) &&
           IsAllLowerHex(value.AsSpan(7));
  }

  internal static bool IsValidOptionalImageReference(string? reference)
  {
    if (reference is null)
    {
      return true;
    }
    if (reference.Length is < 1 or > 512)
    {
      return false;
    }
    foreach (var character in reference)
    {
      if (character is ' ' or '\t' or '\r' or '\n' or '\"' or '\\' ||
          char.IsControl(character))
      {
        return false;
      }
    }
    return true;
  }

  internal static bool IsAllLowerHex(ReadOnlySpan<char> value)
  {
    foreach (var character in value)
    {
      if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
      {
        return false;
      }
    }
    return true;
  }

  /// <summary>Validates a manager observation using the shared ingestion contract.</summary>
  /// <param name="profile">Deserialized observation, or null for missing state.</param>
  /// <returns>Whether the observation satisfies the supported semantic contract.</returns>
  public static bool IsValid(ManagerObservedState? profile)
  {
    if (profile is null ||
        profile.SchemaVersion != 1 ||
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
        profile.Slots.Any(slot => slot is null) ||
        profile.ConfiguredSlots is < 0 ||
        !IsValidResourcePolicy(profile.ResourcePolicy) ||
        !IsValidWorkerUpdate(profile) ||
        !IsValidHostHardware(profile) ||
        !IsValidHostAdmission(profile) ||
        !IsValidSourceObservations(profile) ||
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

  private static bool IsValidHostAdmission(
      ManagerObservedState profile)
  {
    var admission = profile.HostAdmission;
    if (admission is null)
    {
      return profile.ManagerContractVersion < 18;
    }
    if (admission.Status is not (
            "disabled" or
            "available" or
            "degraded" or
            "unavailable") ||
        !IsHostAdmissionNamespace(admission.Namespace) ||
        admission.Epoch is < 0 ||
        admission.DecisionSequence is < 0 ||
        admission.CapacityUnits is <= 0 ||
        admission.SafetyMarginUnits is < 0 ||
        admission.EffectiveTotalUnits is <= 0 ||
        admission.AvailableUnits is < 0 ||
        admission.AvailableUnits > admission.EffectiveTotalUnits ||
        !IsHostAdmissionToken(admission.HostPolicyFingerprint) ||
        !IsValidHostAdmissionAccounting(
            profile.ManagerContractVersion,
            admission.Accounting) ||
        !IsValidHostAdmissionDecision(admission.LastDecision))
    {
      return false;
    }
    if (admission.CapacityUnits is { } capacity &&
        admission.SafetyMarginUnits is { } margin &&
        admission.EffectiveTotalUnits != capacity - margin)
    {
      return false;
    }

    return admission.Status switch
    {
      "disabled" =>
          admission.Namespace is null &&
          admission.Epoch is null &&
          admission.DecisionSequence is null &&
          admission.CapacityUnits is null &&
          admission.SafetyMarginUnits is null &&
          admission.EffectiveTotalUnits is null &&
          admission.AvailableUnits is null &&
          admission.HostPolicyFingerprint is null &&
          admission.Accounting is null &&
          admission.LastDecision is null,
      "unavailable" =>
          admission.Namespace is not null &&
          admission.Epoch is null &&
          admission.DecisionSequence is null &&
          admission.CapacityUnits is null &&
          admission.SafetyMarginUnits is null &&
          admission.EffectiveTotalUnits is null &&
          admission.AvailableUnits is null &&
          admission.HostPolicyFingerprint is null &&
          admission.Accounting is null &&
          admission.LastDecision is null,
      "available" =>
          admission.Namespace is not null &&
          admission.Epoch is not null &&
          admission.DecisionSequence is not null &&
          admission.CapacityUnits is not null &&
          admission.SafetyMarginUnits is not null &&
          admission.EffectiveTotalUnits is not null &&
          admission.AvailableUnits is not null &&
          admission.HostPolicyFingerprint is not null &&
          admission.Accounting is
          {
            ProfilePolicyFingerprint: not null,
            PendingUnits: not null,
            WithheldUnits: not null,
          },
      _ =>
          admission.Namespace is not null &&
          admission.Epoch is not null &&
          admission.DecisionSequence is not null &&
          admission.EffectiveTotalUnits is not null &&
          admission.AvailableUnits is not null,
    };
  }

  private static bool IsValidHostAdmissionAccounting(
      int managerContractVersion,
      HostAdmissionAccounting? accounting)
  {
    if (accounting is null)
    {
      return true;
    }

    if (accounting.UnitCost <= 0 ||
        accounting.ReservedUnits < 0 ||
        !IsHostAdmissionToken(accounting.ProfilePolicyFingerprint) ||
        accounting.ActiveUnits < 0 ||
        accounting.ProvisionalUnits < 0 ||
        accounting.HeldUnits !=
            accounting.ActiveUnits + accounting.ProvisionalUnits ||
        accounting.BorrowedUnits !=
            Math.Max(accounting.HeldUnits - accounting.ReservedUnits, 0) ||
        !(accounting.PendingUnits is null &&
          accounting.WithheldUnits is null ||
          accounting.PendingUnits is >= 0 &&
          accounting.WithheldUnits == accounting.PendingUnits))
    {
      return false;
    }

    if (managerContractVersion < 19)
    {
      return accounting.AllocatableUnits is null &&
          accounting.AllocatableWorkers is null &&
          accounting.TheoreticalMaximumUnits is null &&
          accounting.TheoreticalMaximumWorkers is null &&
          accounting.WithholdingReason is null;
    }
    if (!accounting.HasCompleteContractNineteenEvidence)
    {
      return false;
    }

    var allUnavailable = accounting.AllocatableUnits is null &&
        accounting.AllocatableWorkers is null &&
        accounting.TheoreticalMaximumUnits is null &&
        accounting.TheoreticalMaximumWorkers is null &&
        accounting.WithholdingReason is null;
    if (allUnavailable)
    {
      return true;
    }
    if (accounting.AllocatableUnits is not { } allocatableUnits ||
        accounting.AllocatableWorkers is not { } allocatableWorkers ||
        accounting.TheoreticalMaximumUnits is not { } theoreticalUnits ||
        accounting.TheoreticalMaximumWorkers is not { } theoreticalWorkers ||
        allocatableUnits < 0 ||
        allocatableWorkers < 0 ||
        theoreticalUnits < 0 ||
        theoreticalWorkers < 0 ||
        allocatableWorkers != allocatableUnits / accounting.UnitCost ||
        theoreticalWorkers != theoreticalUnits / accounting.UnitCost ||
        allocatableUnits > theoreticalUnits ||
        accounting.WithholdingReason is not null and not (
            "budget-exhausted" or
            "protected-reservation" or
            "fair-share-contention" or
            "adoption-pending"))
    {
      return false;
    }

    return accounting.WithholdingReason is null ||
        allocatableUnits == 0 && allocatableWorkers == 0;
  }

  private static bool IsValidHostAdmissionDecision(
      HostAdmissionDecision? decision) =>
      decision is null ||
      decision.Sequence >= 0 &&
      decision.Command is (
          "acquire" or
          "adopt" or
          "renew" or
          "activate" or
          "release" or
          "reconcile") &&
      (decision.FailureCategory is null ||
       IsLowerKebabCode(decision.FailureCategory, 64)) &&
      decision.DecidedAtUnixNano >= 0;

  private static bool IsHostAdmissionNamespace(string? value) =>
      value is null ||
      value.Length is >= 1 and <= 32 &&
      value[0] is >= 'a' and <= 'z' &&
      value.All(character =>
          character is >= 'a' and <= 'z' or
              >= '0' and <= '9' or
              '-');

  private static bool IsHostAdmissionToken(string? value) =>
      value is null ||
      value.Length is >= 1 and <= 128 &&
      value.All(character =>
          character is >= 'a' and <= 'z' or
              >= 'A' and <= 'Z' or
              >= '0' and <= '9' or
              '_' or
              '-');

  internal static bool IsLowerKebabCode(
      string value,
      int maximumLength) =>
      value.Length is >= 1 &&
      value.Length <= maximumLength &&
      value[0] is >= 'a' and <= 'z' &&
      value[^1] != '-' &&
      value.All(character =>
          character is >= 'a' and <= 'z' or
              >= '0' and <= '9' or
              '-') &&
      !value.Contains("--", StringComparison.Ordinal);

  internal static bool IsValidProtocolProfileContracts(
      int protocolVersion,
      IReadOnlyList<ManagerObservedState> profiles) =>
      profiles.All(profile =>
          profile.ManagerContractVersion switch
          {
            >= 21 => protocolVersion >= 12,
            >= 15 => protocolVersion >= 8,
            >= 14 => protocolVersion >= 7,
            _ => true,
          });

  internal static bool IsValidProfileInventory(
      int protocolVersion,
      ConnectorProfileInventory? inventory,
      int profileCount)
  {
    if (protocolVersion < 12)
    {
      return inventory is null;
    }
    if (inventory is null ||
        inventory.ObservedAt == default ||
        inventory.Coverage is not (
            "complete" or
            "partial" or
            "unavailable"))
    {
      return false;
    }
    return inventory.Coverage switch
    {
      "complete" => inventory.UnavailableReason is null,
      "partial" => profileCount > 0 &&
          inventory.UnavailableReason is not null &&
          IsLowerKebabCode(inventory.UnavailableReason, 128),
      _ => profileCount == 0 &&
          inventory.UnavailableReason is not null &&
          IsLowerKebabCode(inventory.UnavailableReason, 128),
    };
  }

  private static bool IsValidSourceObservations(
      ManagerObservedState profile)
  {
    if (profile.SourceObservations is null)
    {
      return profile.ManagerContractVersion < 21;
    }
    if (profile.ManagerContractVersion < 21)
    {
      return false;
    }

    var observations = profile.SourceObservations;
    return IsValidSourceObservation(
            observations.LocalRuntime,
            "local-runtime",
            profile.ManagerInstanceId,
            profile.ObservedAt) &&
        IsValidSourceObservation(
            observations.GitHubScaleSet,
            "github-scale-set",
            profile.ManagerInstanceId,
            profile.ObservedAt) &&
        IsValidSourceObservation(
            observations.ResourceTelemetry,
            "resource-telemetry",
            profile.ManagerInstanceId,
            profile.ObservedAt) &&
        IsValidSourceObservation(
            observations.HostHardware,
            "host-hardware",
            profile.ManagerInstanceId,
            profile.ObservedAt) &&
        IsValidSourceObservation(
            observations.HostAdmission,
            "host-admission",
            profile.ManagerInstanceId,
            profile.ObservedAt) &&
        IsValidSourceObservation(
            observations.SubsystemHealth,
            "subsystem-health",
            profile.ManagerInstanceId,
            profile.ObservedAt) &&
        IsValidSourceObservation(
            observations.Capacity,
            "capacity",
            profile.ManagerInstanceId,
            profile.ObservedAt) &&
        IsValidSourceObservation(
            observations.Workload,
            "workload",
            profile.ManagerInstanceId,
            profile.ObservedAt);
  }

  private static bool IsValidSourceObservation(
      ManagerSourceObservation observation,
      string expectedSource,
      string expectedIdentity,
      DateTimeOffset publishedAt)
  {
    if (observation is null ||
        !string.Equals(
            observation.Authority,
            "pitcrew-manager",
            StringComparison.Ordinal) ||
        !string.Equals(
            observation.Source,
            expectedSource,
            StringComparison.Ordinal) ||
        !string.Equals(
            observation.SourceIdentity,
            expectedIdentity,
            StringComparison.Ordinal) ||
        observation.ObservedAt > publishedAt ||
        observation.Coverage is not (
            "complete" or
            "partial" or
            "unavailable") ||
        observation.Retention is not ("live" or "last-known"))
    {
      return false;
    }

    return observation.Coverage switch
    {
      "complete" when observation.Retention == "live" =>
          observation.ObservedAt is not null &&
          observation.Reason is null,
      "complete" =>
          observation.ObservedAt is not null &&
          observation.Reason == "stale",
      "partial" =>
          observation.ObservedAt is not null &&
          observation.Reason == "source-partial",
      _ =>
          observation.ObservedAt is null &&
          observation.Retention == "live" &&
          observation.Reason is (
              "not-observed" or
              "source-unavailable" or
              "unsupported"),
    };
  }

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
      if (target is null ||
          string.IsNullOrWhiteSpace(target.Key) ||
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
