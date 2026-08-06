using System.ComponentModel.DataAnnotations;

using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Configures connector enrollment, synchronization cadence, and node freshness.
/// </summary>
[Options("PitCrew:Dashboard", ValidateOnStart = true)]
public sealed class FleetDashboardOptions
{
  /// <summary>
  /// Gets or sets the connector polling recommendation returned after successful synchronization.
  /// </summary>
  [Range(5, 3600)]
  public int ConnectorPollSeconds { get; set; } = 15;

  /// <summary>
  /// Gets or sets the maximum heartbeat age considered online.
  /// </summary>
  [Range(10, 86400)]
  public int NodeOfflineAfterSeconds { get; set; } = 60;

  /// <summary>
  /// Gets or sets how often the dashboard evaluates credential-free alert evidence.
  /// </summary>
  [Range(5, 3600)]
  public int AlertEvaluationSeconds { get; set; } = 30;

  /// <summary>
  /// Gets or sets how long a level condition must remain proven before it triggers.
  /// </summary>
  [Range(0, 86400)]
  public int AlertDebounceSeconds { get; set; } = 120;

  /// <summary>
  /// Gets or sets how old a profile observation may become before the manager is considered stale.
  /// </summary>
  [Range(10, 86400)]
  public int AlertManagerStaleAfterSeconds { get; set; } = 120;

  /// <summary>
  /// Gets or sets the consecutive failure count required for repeated-operation alerts.
  /// </summary>
  [Range(2, 1000)]
  public int AlertRepeatedFailureCount { get; set; } = 3;

  /// <summary>
  /// Gets or sets the recent telemetry window inspected for sustained resource pressure.
  /// </summary>
  [Range(1, 1440)]
  public int AlertResourceWindowMinutes { get; set; } = 15;

  /// <summary>
  /// Gets or sets how many newest measurements must all prove pressure.
  /// </summary>
  [Range(2, 100)]
  public int AlertResourcePressureSamples { get; set; } = 4;

  /// <summary>
  /// Gets or sets the host CPU percentage that constitutes sustained pressure.
  /// </summary>
  [Range(1, 100)]
  public int AlertCpuPressurePercent { get; set; } = 90;

  /// <summary>
  /// Gets or sets the host memory percentage that constitutes sustained pressure.
  /// </summary>
  [Range(1, 100)]
  public int AlertMemoryPressurePercent { get; set; } = 90;

  /// <summary>
  /// Gets or sets the PSI percentage that constitutes sustained CPU, memory, or I/O pressure.
  /// </summary>
  [Range(1, 100)]
  public int AlertPressureStallPercent { get; set; } = 20;

  /// <summary>
  /// Gets or sets the sustained aggregate worker-network rate threshold, or zero to disable that diagnosis.
  /// </summary>
  public long AlertNetworkBytesPerSecond { get; set; }

  /// <summary>
  /// Gets or sets the sustained aggregate worker block-I/O rate threshold, or zero to disable that diagnosis.
  /// </summary>
  public long AlertBlockIoBytesPerSecond { get; set; }

  /// <summary>
  /// Gets or sets how long resolved incident history is retained.
  /// </summary>
  [Range(1, 3650)]
  public int AlertIncidentRetentionDays { get; set; } = 90;

  /// <summary>
  /// Gets or sets the hard retained resolved-incident ceiling for one tenant.
  /// </summary>
  [Range(10, 1_000_000)]
  public int MaximumResolvedAlertIncidentsPerTenant { get; set; } = 10_000;

  /// <summary>
  /// Gets or sets the maximum visible incidents returned by one request.
  /// </summary>
  [Range(10, 1000)]
  public int MaximumAlertIncidentsPerQuery { get; set; } = 200;

  /// <summary>
  /// Gets or sets the lifetime of a one-time connector enrollment code.
  /// </summary>
  [Range(1, 1440)]
  public int EnrollmentCodeLifetimeMinutes { get; set; } = 15;

  /// <summary>
  /// Gets or sets how long a queued capacity command remains deliverable.
  /// </summary>
  [Range(1, 1440)]
  public int CapacityCommandLifetimeMinutes { get; set; } = 10;

  /// <summary>
  /// Gets or sets when an unacknowledged delivered command becomes eligible for redelivery.
  /// </summary>
  [Range(30, 3600)]
  public int CapacityCommandRedeliverySeconds { get; set; } = 120;

  /// <summary>
  /// Gets or sets how long a queued manager-recovery command remains deliverable.
  /// </summary>
  [Range(1, 1440)]
  public int RecoveryCommandLifetimeMinutes { get; set; } = 10;

  /// <summary>
  /// Gets or sets when an unclaimed recovery command may be offered again.
  /// </summary>
  [Range(30, 3600)]
  public int RecoveryCommandRedeliverySeconds { get; set; } = 120;

  /// <summary>
  /// Gets or sets the minimum delay between manager-recovery requests for one profile.
  /// </summary>
  [Range(1, 3600)]
  public int RecoveryCommandCooldownSeconds { get; set; } = 60;

  /// <summary>
  /// Gets or sets the oldest connector recovery capability accepted when queueing.
  /// </summary>
  [Range(10, 3600)]
  public int RecoveryCapabilityFreshnessSeconds { get; set; } = 120;

  /// <summary>
  /// Gets or sets how long per-observation historical telemetry samples are retained.
  /// </summary>
  /// <remarks>
  /// A retained sample measures at roughly 444 bytes of checkpointed SQLite growth, measured after
  /// <c>PRAGMA wal_checkpoint(TRUNCATE)</c>. That figure accounts for the sample row plus its
  /// proportional share of hourly rollups, manager events, subsystem health changes,
  /// capacity-deficit evidence, cursor rows, and supporting indexes. A profile polled every fifteen
  /// seconds therefore costs about 2.4 MiB per day and about 17 MiB across this default window.
  /// </remarks>
  [Range(1, 3650)]
  public int TelemetrySampleRetentionDays { get; set; } = 7;

  /// <summary>
  /// Gets or sets how long deterministic hourly telemetry rollups are retained.
  /// </summary>
  [Range(1, 3650)]
  public int TelemetryRollupRetentionDays { get; set; } = 90;

  /// <summary>
  /// Gets or sets how long durable manager events are retained.
  /// </summary>
  [Range(1, 3650)]
  public int ManagerEventRetentionDays { get; set; } = 30;

  /// <summary>
  /// Gets or sets how long retained subsystem-health changes and capacity-deficit observations are kept.
  /// </summary>
  /// <remarks>
  /// Diagnostic rows are written on change, so a subsystem or autoscaling target that stops being
  /// reported would otherwise keep its newest row forever. The newest row per key survives this age
  /// bound only while the profile still reports; once the profile stops reporting, its diagnostic
  /// rows are deleted so an absent key is not preserved indefinitely.
  /// </remarks>
  [Range(1, 3650)]
  public int DiagnosticRetentionDays { get; set; } = 30;

  /// <summary>
  /// Gets or sets the hard per-profile ceiling on retained telemetry samples.
  /// </summary>
  [Range(100, 1_000_000)]
  public int MaximumTelemetrySamplesPerProfile { get; set; } = 60_000;

  /// <summary>
  /// Gets or sets the hard per-profile ceiling on retained manager events.
  /// </summary>
  [Range(100, 1_000_000)]
  public int MaximumManagerEventsPerProfile { get; set; } = 20_000;

  /// <summary>
  /// Gets or sets the hard per-profile ceiling on retained rows of each diagnostic table.
  /// </summary>
  /// <remarks>
  /// Subsystem-health changes and per-target capacity-deficit observations are bounded separately by
  /// this ceiling, so a manager that invents new autoscaling target keys cannot grow the database
  /// without bound.
  /// </remarks>
  [Range(10, 1_000_000)]
  public int MaximumDiagnosticsPerProfile { get; set; } = 5_000;

  /// <summary>
  /// Gets or sets the hard node-wide ceiling on retained telemetry samples.
  /// </summary>
  /// <remarks>
  /// Profile identifier churn cannot bypass this bound because retention sweeps every historical
  /// profile recorded for the node, not only the profiles present in the newest heartbeat.
  /// </remarks>
  [Range(100, 5_000_000)]
  public int MaximumTelemetrySamplesPerNode { get; set; } = 250_000;

  /// <summary>
  /// Gets or sets the hard node-wide ceiling on retained hourly telemetry rollups.
  /// </summary>
  [Range(100, 5_000_000)]
  public int MaximumTelemetryRollupsPerNode { get; set; } = 100_000;

  /// <summary>
  /// Gets or sets the hard node-wide ceiling on retained manager events.
  /// </summary>
  [Range(100, 5_000_000)]
  public int MaximumManagerEventsPerNode { get; set; } = 100_000;

  /// <summary>
  /// Gets or sets the combined node-wide ceiling shared by both retained diagnostic collections.
  /// </summary>
  [Range(200, 5_000_000)]
  public int MaximumDiagnosticsPerNode { get; set; } = 25_000;

  /// <summary>
  /// Gets or sets the hard ceiling on retained profiles for one node.
  /// </summary>
  /// <remarks>
  /// Profile identifier churn cannot accumulate cursors forever: profiles beyond this ceiling, least
  /// recently reported first, lose their retained rows and their cursor.
  /// </remarks>
  [Range(1, 10_000)]
  public int MaximumProfilesPerNode { get; set; } = 200;

  /// <summary>
  /// Gets or sets the hard database-wide ceiling on retained telemetry samples across every node.
  /// </summary>
  [Range(100, 50_000_000)]
  public int MaximumTelemetrySamplesPerDatabase { get; set; } = 5_000_000;

  /// <summary>
  /// Gets or sets the hard database-wide ceiling on retained hourly rollups across every node.
  /// </summary>
  [Range(100, 50_000_000)]
  public int MaximumTelemetryRollupsPerDatabase { get; set; } = 2_000_000;

  /// <summary>
  /// Gets or sets the hard database-wide ceiling on retained manager events across every node.
  /// </summary>
  [Range(100, 50_000_000)]
  public int MaximumManagerEventsPerDatabase { get; set; } = 2_000_000;

  /// <summary>
  /// Gets or sets the combined database-wide ceiling shared by both retained diagnostic collections.
  /// </summary>
  [Range(100, 50_000_000)]
  public int MaximumDiagnosticsPerDatabase { get; set; } = 500_000;

  /// <summary>
  /// Gets or sets the hard database-wide ceiling on retained profile histories across every node.
  /// </summary>
  /// <remarks>
  /// Enroll, sync, and abandon churn cannot grow the database without bound inside the retention
  /// window, because history for the least recently updated profiles beyond this ceiling is deleted
  /// deterministically and replaced by a tombstone that keeps its completeness provenance.
  /// </remarks>
  [Range(1, 1_000_000)]
  public int MaximumProfileHistories { get; set; } = 20_000;

  /// <summary>
  /// Gets or sets the hard database-wide ceiling on how many nodes retain history at once.
  /// </summary>
  [Range(1, 100_000)]
  public int MaximumHistoryNodes { get; set; } = 2_000;

  /// <summary>
  /// Gets or sets the smallest gap between two bounded global history maintenance sweeps.
  /// </summary>
  /// <remarks>
  /// Retention cannot depend only on the node that happens to be syncing, so a bounded global sweep
  /// ages history across abandoned nodes as well. The sweep runs inside the heartbeat transaction at
  /// most once per interval, so an ordinary heartbeat does not pay for it.
  /// </remarks>
  [Range(1, 86_400)]
  public int HistoryGlobalSweepSeconds { get; set; } = 300;

  /// <summary>
  /// Gets or sets how far ahead of dashboard time an observation may claim to be observed.
  /// </summary>
  /// <remarks>
  /// Observations and events beyond this bounded skew allowance are rejected and counted so a
  /// mis-set connector clock cannot create unbounded future rollup buckets.
  /// </remarks>
  [Range(0, 86_400)]
  public int HistoryClockSkewToleranceSeconds { get; set; } = 300;

  /// <summary>
  /// Gets or sets the default served history range when a caller supplies no bounds.
  /// </summary>
  [Range(1, 8760)]
  public int DefaultHistoryRangeHours { get; set; } = 24;

  /// <summary>
  /// Gets or sets the widest span and furthest lookback one bounded history query may request.
  /// </summary>
  [Range(1, 8760)]
  public int MaximumHistoryRangeHours { get; set; } = 2160;

  /// <summary>
  /// Gets or sets the maximum samples or rollups one bounded query returns per profile.
  /// </summary>
  [Range(10, 5000)]
  public int MaximumHistoryPoints { get; set; } = 1000;

  /// <summary>
  /// Gets or sets the maximum manager events one bounded query returns per profile.
  /// </summary>
  [Range(10, 5000)]
  public int MaximumHistoryEvents { get; set; } = 200;

  /// <summary>
  /// Gets or sets the maximum diagnostic rows one bounded query returns per profile.
  /// </summary>
  /// <remarks>
  /// Applied separately to retained subsystem-health changes and to retained per-target
  /// capacity-deficit observations so neither hides the other.
  /// </remarks>
  [Range(10, 5000)]
  public int MaximumHistoryDiagnostics { get; set; } = 200;

  /// <summary>
  /// Gets or sets the maximum samples or rollups one bounded query returns for the whole node.
  /// </summary>
  [Range(10, 20_000)]
  public int MaximumNodeHistoryPoints { get; set; } = 5000;

  /// <summary>
  /// Gets or sets the maximum manager events one bounded query returns for the whole node.
  /// </summary>
  [Range(10, 20_000)]
  public int MaximumNodeHistoryEvents { get; set; } = 1000;

  /// <summary>
  /// Gets or sets the maximum diagnostic rows one bounded query returns for the whole node.
  /// </summary>
  /// <remarks>
  /// This is one combined budget shared by retained subsystem-health changes and retained
  /// capacity-deficit observations, so the advertised node-wide cap is never doubled.
  /// </remarks>
  [Range(20, 20_000)]
  public int MaximumNodeHistoryDiagnostics { get; set; } = 1000;

  /// <summary>
  /// Validates relationships between connector polling and dashboard freshness settings.
  /// </summary>
  /// <returns>Cross-property validation failures.</returns>
  public IEnumerable<ValidationError> Validate()
  {
    if (ConnectorPollSeconds * 2 > NodeOfflineAfterSeconds)
    {
      yield return
          "NodeOfflineAfterSeconds must be at least twice ConnectorPollSeconds.";
    }
    if (ConnectorPollSeconds * 2 > AlertManagerStaleAfterSeconds)
    {
      yield return
          "AlertManagerStaleAfterSeconds must be at least twice ConnectorPollSeconds.";
    }
    if (AlertNetworkBytesPerSecond < 0)
    {
      yield return "AlertNetworkBytesPerSecond cannot be negative.";
    }
    if (AlertBlockIoBytesPerSecond < 0)
    {
      yield return "AlertBlockIoBytesPerSecond cannot be negative.";
    }
    if (AlertResourceWindowMinutes * 60 <
        ConnectorPollSeconds * AlertResourcePressureSamples)
    {
      yield return
          "AlertResourceWindowMinutes must hold at least AlertResourcePressureSamples connector polls.";
    }
    if (MaximumAlertIncidentsPerQuery >
        MaximumResolvedAlertIncidentsPerTenant)
    {
      yield return
          "MaximumAlertIncidentsPerQuery cannot exceed MaximumResolvedAlertIncidentsPerTenant.";
    }
    if (RecoveryCapabilityFreshnessSeconds < ConnectorPollSeconds * 2)
    {
      yield return
          "RecoveryCapabilityFreshnessSeconds must be at least twice ConnectorPollSeconds.";
    }
    if (TelemetryRollupRetentionDays < TelemetrySampleRetentionDays)
    {
      yield return
          "TelemetryRollupRetentionDays must be at least TelemetrySampleRetentionDays.";
    }
    if (DefaultHistoryRangeHours > MaximumHistoryRangeHours)
    {
      yield return
          "DefaultHistoryRangeHours cannot exceed MaximumHistoryRangeHours.";
    }
    if (MaximumNodeHistoryPoints < MaximumHistoryPoints)
    {
      yield return
          "MaximumNodeHistoryPoints must be at least MaximumHistoryPoints.";
    }
    if (MaximumNodeHistoryEvents < MaximumHistoryEvents)
    {
      yield return
          "MaximumNodeHistoryEvents must be at least MaximumHistoryEvents.";
    }
    if (MaximumTelemetrySamplesPerNode < MaximumTelemetrySamplesPerProfile)
    {
      yield return
          "MaximumTelemetrySamplesPerNode must be at least MaximumTelemetrySamplesPerProfile.";
    }
    if (MaximumManagerEventsPerNode < MaximumManagerEventsPerProfile)
    {
      yield return
          "MaximumManagerEventsPerNode must be at least MaximumManagerEventsPerProfile.";
    }
    if (MaximumNodeHistoryDiagnostics < MaximumHistoryDiagnostics * 2)
    {
      yield return
          "MaximumNodeHistoryDiagnostics must be at least twice MaximumHistoryDiagnostics because it is one combined budget shared by subsystem health and capacity deficits.";
    }
    if (MaximumDiagnosticsPerNode < MaximumDiagnosticsPerProfile * 2)
    {
      yield return
          "MaximumDiagnosticsPerNode must be at least twice MaximumDiagnosticsPerProfile because it is one combined budget shared by subsystem health and capacity deficits.";
    }
    if (MaximumTelemetrySamplesPerDatabase < MaximumTelemetrySamplesPerNode)
    {
      yield return
          "MaximumTelemetrySamplesPerDatabase must be at least MaximumTelemetrySamplesPerNode.";
    }
    if (MaximumTelemetryRollupsPerDatabase < MaximumTelemetryRollupsPerNode)
    {
      yield return
          "MaximumTelemetryRollupsPerDatabase must be at least MaximumTelemetryRollupsPerNode.";
    }
    if (MaximumManagerEventsPerDatabase < MaximumManagerEventsPerNode)
    {
      yield return
          "MaximumManagerEventsPerDatabase must be at least MaximumManagerEventsPerNode.";
    }
    if (MaximumDiagnosticsPerDatabase < MaximumDiagnosticsPerNode)
    {
      yield return
          "MaximumDiagnosticsPerDatabase must be at least MaximumDiagnosticsPerNode.";
    }
    if (MaximumProfileHistories < MaximumProfilesPerNode)
    {
      yield return
          "MaximumProfileHistories must be at least MaximumProfilesPerNode.";
    }
    if (TelemetryRollupRetentionDays * 24 < MaximumHistoryRangeHours)
    {
      yield return
          "TelemetryRollupRetentionDays must cover MaximumHistoryRangeHours, because completeness provenance has to survive every range a caller may legally query.";
    }
  }
}
