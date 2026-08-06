using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet;

internal static class AlertRuleEvaluator
{
  private static readonly HashSet<string> _repeatedOperationAlerts =
  [
      "runner-registration",
      "runner-removal",
      "jit-config-generate",
      "worker-launch",
      "worker-exit",
      "telemetry-sample",
      "observed-state-publish",
      "registration-cleanup",
      "container-cleanup",
      "admission-reserve",
      "admission-settle",
  ];

  private static readonly HashSet<string> _adverseExitClassifications =
  [
      "sigkill",
      "signal",
      "error",
      "launch-failure",
  ];

  private static readonly HashSet<string> _failedCommandStatuses =
  [
      "failed",
      "rejected",
      "expired",
      "indeterminate",
  ];

  internal static AlertEvaluationResult Evaluate(
      AlertEvidenceSnapshot snapshot,
      FleetDashboardOptions options,
      DateTimeOffset evaluatedAt)
  {
    ArgumentNullException.ThrowIfNull(snapshot);
    ArgumentNullException.ThrowIfNull(options);
    var candidates = new List<AlertCandidate>();
    var suppressions = new List<AlertSuppression>();
    foreach (var node in snapshot.Nodes)
    {
      if (node.IsRevoked)
      {
        continue;
      }

      var lastContact = node.LastSeenAt ?? node.EnrolledAt;
      var offlineAt = lastContact.AddSeconds(
          options.NodeOfflineAfterSeconds);
      if (evaluatedAt >= offlineAt)
      {
        candidates.Add(Create(
            node,
            null,
            "connector-offline",
            "critical",
            "node",
            offlineAt,
            options.AlertDebounceSeconds,
            $"{node.DisplayName} connector is offline",
            $"No connector synchronization has been accepted since {lastContact:O}.",
            "connector-offline",
            null));
        suppressions.Add(new AlertSuppression(
            null,
            node.NodeId,
            null,
            null));
        continue;
      }

      EvaluateHostPressure(
          candidates,
          suppressions,
          node,
          options);
      foreach (var profile in node.Profiles)
      {
        EvaluateProfile(
            candidates,
            suppressions,
            node,
            profile,
            options,
            evaluatedAt);
      }
    }

    return new AlertEvaluationResult(
        candidates
            .OrderBy(candidate => candidate.Key, StringComparer.Ordinal)
            .ToArray(),
        suppressions
            .Distinct()
            .ToArray());
  }

  private static void EvaluateProfile(
      ICollection<AlertCandidate> candidates,
      ICollection<AlertSuppression> suppressions,
      AlertNodeEvidence node,
      AlertProfileEvidence profile,
      FleetDashboardOptions options,
      DateTimeOffset evaluatedAt)
  {
    var observation = profile.Observation;
    var staleAt = observation.ObservedAt.AddSeconds(
        options.AlertManagerStaleAfterSeconds);
    if (evaluatedAt >= staleAt)
    {
      candidates.Add(Create(
          node,
          observation.ProfileId,
          "manager-stale",
          "critical",
          "profile",
          staleAt,
          options.AlertDebounceSeconds,
          $"{observation.ProfileId} manager evidence is stale",
          $"The latest manager observation is from {observation.ObservedAt:O}.",
          "manager-observation-stale",
          null));
      EvaluateCommands(candidates, node, profile);
      SuppressManagerDiagnoses(
          suppressions,
          node,
          observation.ProfileId);
      return;
    }

    if (!string.Equals(
        observation.ManagerStatus,
        "running",
        StringComparison.Ordinal))
    {
      var severity = string.Equals(
          observation.ManagerStatus,
          "stopped",
          StringComparison.Ordinal)
          ? "critical"
          : "warning";
      candidates.Add(Create(
          node,
          observation.ProfileId,
          "manager-unavailable",
          severity,
          "profile",
          observation.ObservedAt,
          options.AlertDebounceSeconds,
          $"{observation.ProfileId} manager is {observation.ManagerStatus}",
          "The manager is not reporting a running lifecycle state.",
          $"manager-{observation.ManagerStatus}",
          null));
    }

    EvaluateSubsystems(
        candidates,
        suppressions,
        node,
        profile,
        options);
    EvaluateOperations(
        candidates,
        suppressions,
        node,
        profile,
        options);
    EvaluateWorkers(candidates, node, profile, options);
    EvaluateCapacity(
        candidates,
        suppressions,
        node,
        profile,
        options);
    EvaluateJournal(
        candidates,
        suppressions,
        node,
        profile,
        options);
    EvaluateCommands(candidates, node, profile);
    EvaluateResources(
        candidates,
        suppressions,
        node,
        profile,
        options);
  }

  private static void EvaluateSubsystems(
      ICollection<AlertCandidate> candidates,
      ICollection<AlertSuppression> suppressions,
      AlertNodeEvidence node,
      AlertProfileEvidence profile,
      FleetDashboardOptions options)
  {
    var health = profile.Observation.SubsystemHealth;
    if (health is null)
    {
      suppressions.Add(new AlertSuppression(
          null,
          node.NodeId,
          profile.Observation.ProfileId,
          "subsystem-failure"));
      return;
    }

    EvaluateSubsystem(
        candidates,
        node,
        profile.Observation.ProfileId,
        "docker",
        health.Docker,
        options);
    EvaluateSubsystem(
        candidates,
        node,
        profile.Observation.ProfileId,
        "github",
        health.Github,
        options);
  }

  private static void EvaluateSubsystem(
      ICollection<AlertCandidate> candidates,
      AlertNodeEvidence node,
      string profileId,
      string subsystem,
      SubsystemHealthSummary summary,
      FleetDashboardOptions options)
  {
    if (summary.State is not ("degraded" or "unavailable") ||
        summary.State == "degraded" &&
        summary.ConsecutiveFailures < options.AlertRepeatedFailureCount)
    {
      return;
    }

    var failure = summary.LastFailure;
    var firstObservedAt = failure?.ObservedAt ?? summary.ObservedAt;
    var operation = failure?.Operation ?? subsystem;
    var reason = failure?.Reason ?? $"{subsystem}-{summary.State}";
    candidates.Add(Create(
        node,
        profileId,
        "subsystem-failure",
        summary.State == "unavailable" ? "critical" : "warning",
        subsystem,
        firstObservedAt,
        options.AlertDebounceSeconds,
        $"{profileId} {subsystem} operations are {summary.State}",
        $"{summary.ConsecutiveFailures} consecutive {subsystem} failures; latest operation: {operation}.",
        reason,
        failure?.Evidence));
  }

  private static void EvaluateOperations(
      ICollection<AlertCandidate> candidates,
      ICollection<AlertSuppression> suppressions,
      AlertNodeEvidence node,
      AlertProfileEvidence profile,
      FleetDashboardOptions options)
  {
    var events = profile.Observation.OperationJournal?.Events;
    if (events is null)
    {
      suppressions.Add(new AlertSuppression(
          null,
          node.NodeId,
          profile.Observation.ProfileId,
          "manager-operation-failure"));
      return;
    }

    foreach (var managerEvent in events
        .Where(item => _repeatedOperationAlerts.Contains(item.Operation))
        .GroupBy(
            item => $"{item.Operation}\n{item.Target}",
            StringComparer.Ordinal)
        .Select(group => group.MaxBy(item => item.Sequence))
        .Where(item => item is not null)
        .Cast<ManagerEvent>())
    {
      if (managerEvent.Outcome is not (
              "failed" or
              "timed-out" or
              "blocked" or
              "retry-scheduled"))
      {
        continue;
      }
      var failures = Math.Max(
          managerEvent.ConsecutiveFailures ?? 0,
          managerEvent.Attempt ?? 0);
      if (failures < options.AlertRepeatedFailureCount)
      {
        continue;
      }

      var severity = managerEvent.Operation is (
          "telemetry-sample" or
          "observed-state-publish" or
          "container-cleanup" or
          "registration-cleanup")
          ? "warning"
          : "critical";
      var target = managerEvent.Target is null
          ? string.Empty
          : $" for {managerEvent.Target}";
      candidates.Add(Create(
          node,
          profile.Observation.ProfileId,
          "manager-operation-failure",
          severity,
          $"{managerEvent.Operation}|{managerEvent.Target}",
          managerEvent.ObservedAt,
          options.AlertDebounceSeconds,
          $"{profile.Observation.ProfileId} {managerEvent.Operation} keeps failing",
          $"{failures} failed attempts{target}; outcome: {managerEvent.Outcome}.",
          managerEvent.Reason,
          managerEvent.Evidence));
    }
  }

  private static void EvaluateWorkers(
      ICollection<AlertCandidate> candidates,
      AlertNodeEvidence node,
      AlertProfileEvidence profile,
      FleetDashboardOptions options)
  {
    foreach (var slot in profile.Observation.Slots.Where(
        slot => !slot.ProcessRunning))
    {
      var lastExit = slot.LastExit;
      if (lastExit?.Classification == "oom-killed" &&
          lastExit.DockerOomKilled is true)
      {
        candidates.Add(Create(
            node,
            profile.Observation.ProfileId,
            "worker-oom",
            "critical",
            $"{slot.Key}|{lastExit.ObservedAt:O}",
            lastExit.ObservedAt,
            TimeSpan.Zero,
            $"{profile.Observation.ProfileId} worker was OOM-killed",
            $"Slot {slot.Key} has Docker-confirmed out-of-memory exit evidence.",
            "oom-killed",
            lastExit.Evidence));
        continue;
      }
      if (lastExit is not null &&
          _adverseExitClassifications.Contains(lastExit.Classification))
      {
        candidates.Add(Create(
            node,
            profile.Observation.ProfileId,
            "worker-exit",
            lastExit.Classification is "error" or "launch-failure"
                ? "critical"
                : "warning",
            $"{slot.Key}|{lastExit.ObservedAt:O}",
            lastExit.ObservedAt,
            options.AlertDebounceSeconds,
            $"{profile.Observation.ProfileId} worker exited abnormally",
            $"Slot {slot.Key} last exited as {lastExit.Classification}.",
            lastExit.Classification,
            lastExit.Evidence));
        continue;
      }
      if (slot.FailureCount >= options.AlertRepeatedFailureCount)
      {
        candidates.Add(Create(
            node,
            profile.Observation.ProfileId,
            "worker-failure",
            "critical",
            slot.Key,
            slot.UpdatedAt ?? profile.Observation.ObservedAt,
            options.AlertDebounceSeconds,
            $"{profile.Observation.ProfileId} worker cannot start",
            $"Slot {slot.Key} has {slot.FailureCount} consecutive failures.",
            "worker-failure-backoff",
            $"Backoff: {slot.BackoffSeconds.ToString(CultureInfo.InvariantCulture)} seconds."));
      }
    }
  }

  private static void EvaluateCapacity(
      ICollection<AlertCandidate> candidates,
      ICollection<AlertSuppression> suppressions,
      AlertNodeEvidence node,
      AlertProfileEvidence profile,
      FleetDashboardOptions options)
  {
    var capacity = profile.Observation.CapacityEvidence;
    if (capacity is null)
    {
      suppressions.Add(new AlertSuppression(
          null,
          node.NodeId,
          profile.Observation.ProfileId,
          "capacity-deficit"));
      return;
    }
    if (capacity.Fixed is not null)
    {
      if (string.Equals(
          capacity.Fixed.Freshness,
          "current",
          StringComparison.Ordinal))
      {
        EvaluateDeficit(
            candidates,
            node,
            profile.Observation.ProfileId,
            "fixed",
            capacity.Fixed,
            options);
      }
      else
      {
        suppressions.Add(ExactSuppression(
            node,
            profile.Observation.ProfileId,
            "capacity-deficit",
            "fixed"));
      }
    }
    foreach (var target in capacity.Targets)
    {
      if (string.Equals(
          target.Freshness,
          "current",
          StringComparison.Ordinal))
      {
        EvaluateDeficit(
            candidates,
            node,
            profile.Observation.ProfileId,
            target.Key,
            target,
            options);
      }
      else
      {
        suppressions.Add(ExactSuppression(
            node,
            profile.Observation.ProfileId,
            "capacity-deficit",
            target.Key));
      }
    }
  }

  private static void EvaluateDeficit(
      ICollection<AlertCandidate> candidates,
      AlertNodeEvidence node,
      string profileId,
      string targetKey,
      CapacityDeficitEvidence deficit,
      FleetDashboardOptions options)
  {
    if (deficit.LocalDeficit <= 0 &&
        deficit.EligibilityDeficit is null or <= 0)
    {
      return;
    }

    var critical = deficit.Reason is (
        "docker-unavailable" or
        "docker-failed" or
        "jit-failed" or
        "listener-unavailable" or
        "session-unavailable" or
        "invalid-desired-state");
    candidates.Add(Create(
        node,
        profileId,
        "capacity-deficit",
        critical ? "critical" : "warning",
        targetKey,
        deficit.ObservedAt,
        options.AlertDebounceSeconds,
        $"{profileId} capacity is below target",
        $"Target {targetKey} reports local deficit {deficit.LocalDeficit} and eligibility deficit {deficit.EligibilityDeficit?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.",
        deficit.Reason,
        deficit.Evidence));
  }

  private static void EvaluateJournal(
      ICollection<AlertCandidate> candidates,
      ICollection<AlertSuppression> suppressions,
      AlertNodeEvidence node,
      AlertProfileEvidence profile,
      FleetDashboardOptions options)
  {
    var journal = profile.Journal;
    if (string.Equals(
        journal.Status,
        "unreported",
        StringComparison.Ordinal))
    {
      suppressions.Add(ExactSuppression(
          node,
          profile.Observation.ProfileId,
          "journal-unavailable",
          "journal"));
    }
    if (journal.Status is "unavailable" or "truncated")
    {
      candidates.Add(Create(
          node,
          profile.Observation.ProfileId,
          "journal-unavailable",
          journal.Status == "unavailable" ? "critical" : "warning",
          "journal",
          profile.Observation.ObservedAt,
          options.AlertDebounceSeconds,
          $"{profile.Observation.ProfileId} operation history is {journal.Status}",
          "The manager journal cannot currently provide a complete retained window.",
          $"journal-{journal.Status}",
          null));
    }
    if (journal.UndeliveredEvents > 0)
    {
      candidates.Add(Create(
          node,
          profile.Observation.ProfileId,
          "journal-undelivered",
          "warning",
          "journal",
          profile.Observation.ObservedAt,
          options.AlertDebounceSeconds,
          $"{profile.Observation.ProfileId} has undelivered manager events",
          $"{journal.UndeliveredEvents} retained manager events have not reached the dashboard.",
          "journal-undelivered",
          null));
    }
    if (journal.MissedEvents > 0 ||
        journal.EpochResets > 0 ||
        journal.ManagerDroppedEvents > 0)
    {
      candidates.Add(Create(
          node,
          profile.Observation.ProfileId,
          "journal-discontinuity",
          "warning",
          "journal",
          profile.Observation.ObservedAt,
          TimeSpan.Zero,
          $"{profile.Observation.ProfileId} operation history is discontinuous",
          $"Missed {journal.MissedEvents}, manager-dropped {journal.ManagerDroppedEvents}, resets {journal.EpochResets}.",
          "journal-discontinuity",
          null));
    }
    if (journal.HistoryExpiredAt is not null)
    {
      candidates.Add(Create(
          node,
          profile.Observation.ProfileId,
          "history-expired",
          "warning",
          "history",
          journal.HistoryExpiredAt.Value,
          TimeSpan.Zero,
          $"{profile.Observation.ProfileId} history has expired",
          "The dashboard deliberately expired earlier retained evidence for this profile.",
          "dashboard-history-expired",
          null));
    }
  }

  private static void EvaluateCommands(
      ICollection<AlertCandidate> candidates,
      AlertNodeEvidence node,
      AlertProfileEvidence profile)
  {
    EvaluateCommand(
        candidates,
        node,
        profile.Observation,
        profile.LatestCapacityCommand);
    EvaluateCommand(
        candidates,
        node,
        profile.Observation,
        profile.LatestRecoveryCommand);
  }

  private static void EvaluateCommand(
      ICollection<AlertCandidate> candidates,
      AlertNodeEvidence node,
      ManagerObservedState observation,
      AlertCommandEvidence? command)
  {
    if (command is null ||
        !_failedCommandStatuses.Contains(command.Status))
    {
      return;
    }

    var critical = command.Status is "failed" or "indeterminate";
    candidates.Add(Create(
        node,
        observation.ProfileId,
        "command-failure",
        critical ? "critical" : "warning",
        $"{command.Kind}|{command.CommandId:D}",
        command.CompletedAt ?? observation.ObservedAt,
        TimeSpan.Zero,
        $"{observation.ProfileId} {command.Kind} operation {command.Status}",
        command.Message ?? $"The {command.Kind} command ended as {command.Status}.",
        command.FailureCategory ?? $"command-{command.Status}",
        command.CommandId.ToString("D")));
  }

  private static void EvaluateResources(
      ICollection<AlertCandidate> candidates,
      ICollection<AlertSuppression> suppressions,
      AlertNodeEvidence node,
      AlertProfileEvidence profile,
      FleetDashboardOptions options)
  {
    var required = options.AlertResourcePressureSamples;
    var samples = profile.RecentResourceSamples
        .OrderBy(sample => sample.ObservedAt)
        .TakeLast(required + 1)
        .ToArray();
    var measurements = samples.TakeLast(required).ToArray();
    var cpuEvaluable = measurements.Length == required &&
        measurements.All(sample =>
            string.Equals(sample.Status, "available", StringComparison.Ordinal) &&
            sample.CpuCores is not null &&
            sample.HostLogicalProcessors is > 0);
    if (!cpuEvaluable)
    {
      suppressions.Add(ExactSuppression(
          node,
          profile.Observation.ProfileId,
          "resource-cpu-pressure",
          "cpu"));
    }

    else if (measurements.All(sample =>
        sample.CpuCores!.Value * 100 >=
            sample.HostLogicalProcessors!.Value *
            options.AlertCpuPressurePercent))
    {
      candidates.Add(Create(
          node,
          profile.Observation.ProfileId,
          "resource-cpu-pressure",
          "warning",
          "cpu",
          measurements[0].ObservedAt,
          TimeSpan.Zero,
          $"{profile.Observation.ProfileId} has sustained CPU pressure",
          $"The newest {required} complete samples are at or above {options.AlertCpuPressurePercent}% of host CPU capacity.",
          "sustained-cpu-pressure",
          null));
    }
    var memoryEvaluable = measurements.Length == required &&
        measurements.All(sample =>
            string.Equals(sample.Status, "available", StringComparison.Ordinal) &&
            sample.MemoryBytes is not null &&
            sample.HostMemoryBytes is > 0);
    if (!memoryEvaluable)
    {
      suppressions.Add(ExactSuppression(
          node,
          profile.Observation.ProfileId,
          "resource-memory-pressure",
          "memory"));
    }
    else if (measurements.All(sample =>
        sample.MemoryBytes!.Value /
            (double)sample.HostMemoryBytes!.Value *
            100 >= options.AlertMemoryPressurePercent))
    {
      candidates.Add(Create(
          node,
          profile.Observation.ProfileId,
          "resource-memory-pressure",
          "critical",
          "memory",
          measurements[0].ObservedAt,
          TimeSpan.Zero,
          $"{profile.Observation.ProfileId} has sustained memory pressure",
          $"The newest {required} complete samples are at or above {options.AlertMemoryPressurePercent}% of host memory capacity.",
          "sustained-memory-pressure",
          null));
    }

    if (!EvaluateRate(
        candidates,
        node,
        profile,
        samples,
        required,
        options.AlertNetworkBytesPerSecond,
        "resource-network-pressure",
        "network",
        "network traffic"))
    {
      suppressions.Add(ExactSuppression(
          node,
          profile.Observation.ProfileId,
          "resource-network-pressure",
          "network"));
    }
    if (!EvaluateRate(
        candidates,
        node,
        profile,
        samples,
        required,
        options.AlertBlockIoBytesPerSecond,
        "resource-block-io-pressure",
        "block-io",
        "block I/O"))
    {
      suppressions.Add(ExactSuppression(
          node,
          profile.Observation.ProfileId,
          "resource-block-io-pressure",
          "block-io"));
    }
  }

  private static void EvaluateHostPressure(
      ICollection<AlertCandidate> candidates,
      ICollection<AlertSuppression> suppressions,
      AlertNodeEvidence node,
      FleetDashboardOptions options)
  {
    var required = options.AlertResourcePressureSamples;
    var measurements = node.RecentHostPressureSamples
        .OrderBy(sample => sample.ObservedAt)
        .TakeLast(required)
        .ToArray();
    if (measurements.Length != required)
    {
      SuppressHostPressure(suppressions, node);
      return;
    }

    var cpuEvaluable = measurements.All(sample =>
        sample.CpuUtilizationPercent is not null ||
        sample.CpuPressureSomeAvg10 is not null ||
        sample.Load1 is not null &&
        sample.LogicalProcessorCount is > 0);
    if (!cpuEvaluable)
    {
      suppressions.Add(ExactNodeSuppression(
          node,
          "host-cpu-pressure",
          "cpu"));
    }
    else if (measurements.All(sample =>
        sample.CpuUtilizationPercent >= options.AlertCpuPressurePercent ||
        sample.CpuPressureSomeAvg10 >= options.AlertPressureStallPercent ||
        sample.Load1 * 100 >=
            sample.LogicalProcessorCount * options.AlertCpuPressurePercent))
    {
      var peakCpu = measurements.Max(sample =>
          sample.CpuUtilizationPercent);
      var peakStall = measurements.Max(sample =>
          sample.CpuPressureSomeAvg10);
      candidates.Add(Create(
          node,
          null,
          "host-cpu-pressure",
          "warning",
          "cpu",
          measurements[0].ObservedAt,
          TimeSpan.Zero,
          $"{node.DisplayName} has sustained Docker-host CPU pressure",
          $"The newest {required} host samples all exceed a CPU utilization, load, or PSI threshold.",
          "sustained-host-cpu-pressure",
          string.Create(
              CultureInfo.InvariantCulture,
              $"peakCpuPercent={peakCpu?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unavailable"};peakCpuPsi={peakStall?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unavailable"}")));
    }

    var memoryEvaluable = measurements.All(sample =>
        sample.MemoryTotalBytes is > 0 &&
        sample.MemoryAvailableBytes is not null ||
        sample.MemoryPressureSomeAvg10 is not null);
    if (!memoryEvaluable)
    {
      suppressions.Add(ExactNodeSuppression(
          node,
          "host-memory-pressure",
          "memory"));
    }
    else if (measurements.All(sample =>
        sample.MemoryTotalBytes is > 0 &&
        sample.MemoryAvailableBytes is not null &&
        (sample.MemoryTotalBytes.Value -
            sample.MemoryAvailableBytes.Value) /
            (double)sample.MemoryTotalBytes.Value *
            100 >= options.AlertMemoryPressurePercent ||
        sample.MemoryPressureSomeAvg10 >=
            options.AlertPressureStallPercent))
    {
      var minimumAvailable = measurements.Min(sample =>
          sample.MemoryAvailableBytes);
      var peakStall = measurements.Max(sample =>
          sample.MemoryPressureSomeAvg10);
      candidates.Add(Create(
          node,
          null,
          "host-memory-pressure",
          "critical",
          "memory",
          measurements[0].ObservedAt,
          TimeSpan.Zero,
          $"{node.DisplayName} has sustained Docker-host memory pressure",
          $"The newest {required} host samples all exceed a memory-use or PSI threshold.",
          "sustained-host-memory-pressure",
          string.Create(
              CultureInfo.InvariantCulture,
              $"minimumAvailableBytes={minimumAvailable?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"};peakMemoryPsi={peakStall?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unavailable"}")));
    }

    var ioEvaluable = measurements.All(sample =>
        sample.IoPressureSomeAvg10 is not null);
    if (!ioEvaluable)
    {
      suppressions.Add(ExactNodeSuppression(
          node,
          "host-io-pressure",
          "io"));
    }
    else if (measurements.All(sample =>
        sample.IoPressureSomeAvg10 >= options.AlertPressureStallPercent))
    {
      var peakStall = measurements.Max(sample =>
          sample.IoPressureSomeAvg10);
      candidates.Add(Create(
          node,
          null,
          "host-io-pressure",
          "warning",
          "io",
          measurements[0].ObservedAt,
          TimeSpan.Zero,
          $"{node.DisplayName} has sustained Docker-host I/O pressure",
          $"The newest {required} host samples are all at or above {options.AlertPressureStallPercent}% I/O PSI.",
          "sustained-host-io-pressure",
          string.Create(
              CultureInfo.InvariantCulture,
              $"peakIoPsi={peakStall?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unavailable"}")));
    }
  }

  private static void SuppressHostPressure(
      ICollection<AlertSuppression> suppressions,
      AlertNodeEvidence node)
  {
    suppressions.Add(ExactNodeSuppression(
        node,
        "host-cpu-pressure",
        "cpu"));
    suppressions.Add(ExactNodeSuppression(
        node,
        "host-memory-pressure",
        "memory"));
    suppressions.Add(ExactNodeSuppression(
        node,
        "host-io-pressure",
        "io"));
  }

  private static bool EvaluateRate(
      ICollection<AlertCandidate> candidates,
      AlertNodeEvidence node,
      AlertProfileEvidence profile,
      IReadOnlyList<AlertResourceSample> samples,
      int required,
      long threshold,
      string kind,
      string subject,
      string label)
  {
    if (threshold <= 0 || samples.Count < required + 1)
    {
      return threshold <= 0;
    }
    if (samples.Any(sample =>
        !string.Equals(
            sample.Status,
            "available",
            StringComparison.Ordinal)))
    {
      return false;
    }

    var intervals = new List<(DateTimeOffset ObservedAt, double Rate)>();
    for (var index = 1; index < samples.Count; index++)
    {
      var previous = kind == "resource-network-pressure"
          ? samples[index - 1].NetworkBytes
          : samples[index - 1].BlockIoBytes;
      var current = kind == "resource-network-pressure"
          ? samples[index].NetworkBytes
          : samples[index].BlockIoBytes;
      var seconds =
          (samples[index].ObservedAt - samples[index - 1].ObservedAt)
          .TotalSeconds;
      if (previous is null ||
          current is null ||
          current < previous ||
          seconds <= 0)
      {
        return false;
      }
      intervals.Add((
          samples[index].ObservedAt,
          (current.Value - previous.Value) / seconds));
    }
    var newest = intervals.TakeLast(required).ToArray();
    if (newest.Length != required ||
        newest.Any(interval => interval.Rate < threshold))
    {
      return true;
    }

    candidates.Add(Create(
        node,
        profile.Observation.ProfileId,
        kind,
        "warning",
        subject,
        samples[^(required + 1)].ObservedAt,
        TimeSpan.Zero,
        $"{profile.Observation.ProfileId} has sustained {label}",
        $"The newest {required} measured intervals are at or above {threshold.ToString(CultureInfo.InvariantCulture)} bytes per second.",
        $"sustained-{subject}-pressure",
        null));
    return true;
  }

  private static AlertCandidate Create(
      AlertNodeEvidence node,
      string? profileId,
      string kind,
      string severity,
      string subject,
      DateTimeOffset firstObservedAt,
      int debounceSeconds,
      string title,
      string summary,
      string reason,
      string? evidence) =>
      Create(
          node,
          profileId,
          kind,
          severity,
          subject,
          firstObservedAt,
          TimeSpan.FromSeconds(debounceSeconds),
          title,
          summary,
          reason,
          evidence);

  private static AlertCandidate Create(
      AlertNodeEvidence node,
      string? profileId,
      string kind,
      string severity,
      string subject,
      DateTimeOffset firstObservedAt,
      TimeSpan debounce,
      string title,
      string summary,
      string reason,
      string? evidence)
  {
    var key = CreateKey(node, profileId, kind, subject);
    var tenant = Uri.EscapeDataString(node.TenantId);
    var nodeId = Uri.EscapeDataString(node.NodeId.ToString("D"));
    var link = profileId is null
        ? $"/tenants/{tenant}/nodes/{nodeId}"
        : $"/tenants/{tenant}/nodes/{nodeId}/profiles/{Uri.EscapeDataString(profileId)}";
    return new AlertCandidate(
        key,
        node.TenantId,
        node.NodeId,
        profileId,
        kind,
        severity,
        firstObservedAt,
        debounce,
        Bound(title, 160),
        Bound(summary, 512),
        Bound(reason, 128),
        evidence is null ? null : Bound(evidence, 512),
        link);
  }

  private static AlertSuppression ExactSuppression(
      AlertNodeEvidence node,
      string profileId,
      string kind,
      string subject) =>
      new(
          CreateKey(node, profileId, kind, subject),
          node.NodeId,
          profileId,
          kind);

  private static AlertSuppression ExactNodeSuppression(
      AlertNodeEvidence node,
      string kind,
      string subject) =>
      new(
          CreateKey(node, null, kind, subject),
          node.NodeId,
          null,
          kind);

  private static void SuppressManagerDiagnoses(
      ICollection<AlertSuppression> suppressions,
      AlertNodeEvidence node,
      string profileId)
  {
    string[] kinds =
    [
        "manager-unavailable",
        "subsystem-failure",
        "manager-operation-failure",
        "worker-oom",
        "worker-exit",
        "worker-failure",
        "capacity-deficit",
        "journal-unavailable",
        "journal-undelivered",
        "journal-discontinuity",
        "history-expired",
        "resource-cpu-pressure",
        "resource-memory-pressure",
        "resource-network-pressure",
        "resource-block-io-pressure",
    ];
    foreach (var kind in kinds)
    {
      suppressions.Add(new AlertSuppression(
          null,
          node.NodeId,
          profileId,
          kind));
    }
  }

  private static string CreateKey(
      AlertNodeEvidence node,
      string? profileId,
      string kind,
      string subject)
  {
    var subjectHash = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
    return $"{kind}|{node.NodeId:D}|{profileId ?? string.Empty}|{subjectHash}";
  }

  private static string Bound(string value, int maximum) =>
      value.Length <= maximum
          ? value
          : value[..maximum];
}
