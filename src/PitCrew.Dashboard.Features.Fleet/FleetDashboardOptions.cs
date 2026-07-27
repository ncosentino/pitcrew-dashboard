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
  /// A retained sample measures at roughly 333 bytes of SQLite growth, so a profile polled every
  /// fifteen seconds costs about 1.9 MB per day and about 13 MB across this default window.
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
  /// Gets or sets the default served history range when a caller supplies no bounds.
  /// </summary>
  [Range(1, 8760)]
  public int DefaultHistoryRangeHours { get; set; } = 24;

  /// <summary>
  /// Gets or sets the widest history range one bounded query may request.
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
  }
}
