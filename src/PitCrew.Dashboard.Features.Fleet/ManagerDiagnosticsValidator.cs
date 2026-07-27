using System.Text.RegularExpressions;

using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Validates manager contract 12 subsystem health, durable operation journal, and capacity-deficit
/// evidence without inferring causes the manager did not report.
/// </summary>
internal static partial class ManagerDiagnosticsValidator
{
  private const int MaximumJournalCapacity = 64;
  private const int MaximumEventDurationMilliseconds = 86_400_000;
  private const int MaximumAttempt = 1000;
  private const int MaximumConsecutiveFailures = 1000;
  private const int MaximumEvidenceLength = 160;
  private const int MaximumTargetKeyLength = 128;
  private const int MaximumCapacityTargets = 64;

  private static readonly string[] _eventSubsystems =
  [
      "docker",
      "registration",
      "scale-set-session",
      "listener",
      "jit",
      "worker-launch",
      "worker-exit",
      "telemetry",
      "reconciliation",
      "cleanup",
      "admission",
      "recovery",
  ];

  private static readonly string[] _eventOperations =
  [
      "docker-ping",
      "docker-run",
      "docker-inspect",
      "docker-remove",
      "docker-events",
      "registration-token-request",
      "runner-registration",
      "runner-removal",
      "session-create",
      "session-refresh",
      "session-delete",
      "message-poll",
      "message-acknowledge",
      "jit-config-generate",
      "worker-launch",
      "worker-exit",
      "telemetry-sample",
      "desired-state-load",
      "desired-state-apply",
      "capacity-acknowledge",
      "observed-state-publish",
      "registration-cleanup",
      "container-cleanup",
      "admission-reserve",
      "admission-settle",
      "manager-start",
      "manager-shutdown",
      "journal-restore",
  ];

  private static readonly string[] _eventReasons =
  [
      "none",
      "docker-unavailable",
      "docker-failed",
      "timeout",
      "rate-limited",
      "authorization-failed",
      "not-found",
      "conflict",
      "invalid-state",
      "capacity-ceiling",
      "retry-backoff",
      "cancelled",
      "recovered",
      "unknown",
  ];

  private static readonly string[] _capacityDeficitReasons =
  [
      "none",
      "admission-ceiling",
      "launch-pending",
      "docker-unavailable",
      "docker-failed",
      "jit-pending",
      "jit-failed",
      "listener-unavailable",
      "session-unavailable",
      "registration-cleanup-pending",
      "worker-draining",
      "invalid-desired-state",
      "retry-backoff",
      "unknown",
  ];

  /// <summary>
  /// Reports whether the contract 12 diagnostics carried by one observation are internally
  /// consistent and consistent with the rest of the observation.
  /// </summary>
  /// <param name="profile">Observation published by one profile manager.</param>
  /// <returns><see langword="true"/> when the diagnostics satisfy the contract.</returns>
  public static bool IsValid(ManagerObservedState profile)
  {
    if (profile.ManagerContractVersion >= 12 &&
        (profile.OperationJournal is null ||
         profile.SubsystemHealth is null ||
         profile.CapacityEvidence is null))
    {
      return false;
    }

    return IsValidJournal(profile.OperationJournal) &&
        IsValidSubsystemHealth(profile.SubsystemHealth) &&
        IsValidCapacityEvidence(profile);
  }

  private static bool IsValidJournal(ManagerOperationJournal? journal)
  {
    if (journal is null)
    {
      return true;
    }
    if (journal.Status is not ("current" or "truncated" or "unavailable") ||
        journal.Capacity is < 1 or > MaximumJournalCapacity ||
        journal.DroppedEvents < 0 ||
        journal.HighestSequence is < 1 ||
        journal.Events is null ||
        journal.Events.Count > MaximumJournalCapacity)
    {
      return false;
    }
    if (journal.Status is "unavailable" &&
        (journal.Events.Count > 0 ||
         journal.HighestSequence is not null))
    {
      return false;
    }
    if (journal.Status is "truncated" &&
        journal.DroppedEvents < 1)
    {
      return false;
    }
    if (journal.Events.Count > 0 &&
        journal.HighestSequence is null)
    {
      return false;
    }

    var sequences = new HashSet<long>();
    foreach (var managerEvent in journal.Events)
    {
      if (!IsValidEvent(managerEvent) ||
          !sequences.Add(managerEvent.Sequence) ||
          managerEvent.Sequence > journal.HighestSequence)
      {
        return false;
      }
    }

    return true;
  }

  private static bool IsValidEvent(ManagerEvent managerEvent)
  {
    if (managerEvent.Sequence < 1 ||
        string.IsNullOrWhiteSpace(managerEvent.ManagerInstanceId) ||
        managerEvent.ManagerInstanceId.Length > 128 ||
        managerEvent.ObservedAt == default ||
        !_eventSubsystems.Contains(managerEvent.Subsystem) ||
        !_eventOperations.Contains(managerEvent.Operation) ||
        !_eventReasons.Contains(managerEvent.Reason) ||
        managerEvent.Outcome is not (
            "succeeded" or
            "failed" or
            "timed-out" or
            "retry-scheduled" or
            "blocked" or
            "recovered" or
            "unknown") ||
        managerEvent.Target is not null &&
        (managerEvent.Target.Length is < 1 or > MaximumTargetKeyLength) ||
        managerEvent.DurationMilliseconds
            is < 0 or > MaximumEventDurationMilliseconds ||
        managerEvent.Attempt is < 1 or > MaximumAttempt ||
        managerEvent.ConsecutiveFailures is < 0 or > MaximumConsecutiveFailures ||
        managerEvent.RetryAt == default(DateTimeOffset) ||
        !IsValidEvidence(managerEvent.Evidence))
    {
      return false;
    }

    return managerEvent.Outcome switch
    {
      "succeeded" => managerEvent.Reason is "none" or "recovered",
      "failed" or "timed-out" or "blocked" => managerEvent.Reason is not "none",
      "retry-scheduled" => managerEvent.RetryAt is not null,
      _ => true,
    };
  }

  private static bool IsValidSubsystemHealth(ManagerSubsystemHealth? health) =>
      health is null ||
      IsValidSubsystemSummary(health.Docker) &&
      IsValidSubsystemSummary(health.Github);

  private static bool IsValidSubsystemSummary(SubsystemHealthSummary? summary)
  {
    if (summary is null ||
        summary.State is not (
            "healthy" or
            "degraded" or
            "unavailable" or
            "unknown") ||
        summary.ObservedAt == default ||
        summary.ConsecutiveFailures is < 0 or > MaximumConsecutiveFailures ||
        summary.RetryAt == default(DateTimeOffset) ||
        !IsValidOperationEvidence(summary.LastSuccess) ||
        !IsValidOperationEvidence(summary.LastFailure))
    {
      return false;
    }

    return summary.State switch
    {
      "healthy" => summary.ConsecutiveFailures == 0 &&
          summary.LastSuccess is not null,
      "degraded" or "unavailable" => summary.ConsecutiveFailures >= 1 &&
          summary.LastFailure is not null,
      _ => summary.ConsecutiveFailures == 0 &&
          summary.LastSuccess is null &&
          summary.LastFailure is null &&
          summary.RetryAt is null,
    };
  }

  private static bool IsValidOperationEvidence(
      SubsystemOperationEvidence? evidence) =>
      evidence is null ||
      _eventOperations.Contains(evidence.Operation) &&
      _eventReasons.Contains(evidence.Reason) &&
      evidence.ObservedAt != default &&
      evidence.DurationMilliseconds
          is not (< 0 or > MaximumEventDurationMilliseconds) &&
      IsValidEvidence(evidence.Evidence);

  private static bool IsValidCapacityEvidence(ManagerObservedState profile)
  {
    var capacityEvidence = profile.CapacityEvidence;
    if (capacityEvidence is null)
    {
      return true;
    }
    if (capacityEvidence.Targets is null ||
        capacityEvidence.Targets.Count > MaximumCapacityTargets)
    {
      return false;
    }

    if (profile.Autoscaling is null)
    {
      // Fixed evidence measures the manager's own accepted slot count, but a manager that
      // cannot measure capacity publishes an unavailable projection with zero target slots
      // while the accepted desired slots stay nonzero, so the two are never compared.
      return capacityEvidence.Fixed is not null &&
          capacityEvidence.Targets.Count == 0 &&
          IsValidDeficit(capacityEvidence.Fixed);
    }
    if (capacityEvidence.Fixed is not null)
    {
      return false;
    }

    var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var deficit in capacityEvidence.Targets)
    {
      // A deficit key is bounded and trimmed by the manager, and per-target evidence and the
      // autoscaling projection are measured independently, so neither the key nor the target
      // slots are required to match a reported activation target exactly.
      if (string.IsNullOrWhiteSpace(deficit.Key) ||
          deficit.Key.Length > MaximumTargetKeyLength ||
          deficit.Repository?.Length > 2048 ||
          !targetKeys.Add(deficit.Key) ||
          !IsValidDeficit(deficit))
      {
        return false;
      }
    }

    return true;
  }

  private static bool IsValidDeficit(CapacityDeficitEvidence deficit)
  {
    if (deficit.ObservedAt == default ||
        deficit.Freshness is not ("current" or "stale" or "unavailable") ||
        !_capacityDeficitReasons.Contains(deficit.Reason) ||
        deficit.TargetSlots < 0 ||
        deficit.ActiveWorkers < 0 ||
        deficit.StartingWorkers < 0 ||
        deficit.DrainingWorkers < 0 ||
        deficit.CleanupPendingWorkers < 0 ||
        deficit.EligibleWorkers is < 0 ||
        deficit.LocalDeficit < 0 ||
        deficit.EligibilityDeficit is < 0 ||
        !IsValidEvidence(deficit.Evidence))
    {
      return false;
    }
    if ((deficit.EligibleWorkers is null) !=
        (deficit.EligibilityDeficit is null))
    {
      return false;
    }
    if (deficit.LocalDeficit >= 1 &&
        deficit.Reason is "none")
    {
      return false;
    }

    return deficit.Freshness is not "unavailable" ||
        deficit.EligibleWorkers is null &&
        deficit.Reason is "unknown";
  }

  private static bool IsValidEvidence(string? evidence) =>
      evidence is null ||
      evidence.Length <= MaximumEvidenceLength &&
      SanitizedEvidencePattern().IsMatch(evidence);

  [GeneratedRegex(
      @"^[A-Za-z0-9][A-Za-z0-9 .,_()'-]*$",
      RegexOptions.CultureInvariant,
      matchTimeoutMilliseconds: 100)]
  private static partial Regex SanitizedEvidencePattern();
}
