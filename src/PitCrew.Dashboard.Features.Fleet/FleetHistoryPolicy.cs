using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Translates dashboard options into bounded history retention and query policy.
/// </summary>
internal static class FleetHistoryPolicy
{
  public static HistoryRetentionPolicy CreateRetention(
      FleetDashboardOptions options) =>
      new(
          TimeSpan.FromDays(options.TelemetrySampleRetentionDays),
          TimeSpan.FromDays(options.TelemetryRollupRetentionDays),
          TimeSpan.FromDays(options.ManagerEventRetentionDays),
          TimeSpan.FromDays(options.DiagnosticRetentionDays),
          options.MaximumTelemetrySamplesPerProfile,
          options.MaximumManagerEventsPerProfile,
          options.MaximumDiagnosticsPerProfile,
          options.MaximumTelemetrySamplesPerNode,
          options.MaximumManagerEventsPerNode,
          options.MaximumTelemetryRollupsPerNode,
          options.MaximumDiagnosticsPerNode,
          options.MaximumProfilesPerNode,
          options.MaximumTelemetrySamplesPerDatabase,
          options.MaximumTelemetryRollupsPerDatabase,
          options.MaximumManagerEventsPerDatabase,
          options.MaximumDiagnosticsPerDatabase,
          options.MaximumProfileHistories,
          options.MaximumHistoryNodes,
          TimeSpan.FromSeconds(options.HistoryGlobalSweepSeconds),
          TimeSpan.FromHours(options.MaximumHistoryRangeHours));

  public static HistoryCapabilities CreateCapabilities(
      FleetDashboardOptions options) =>
      new(
          options.DefaultHistoryRangeHours,
          options.MaximumHistoryRangeHours,
          ["raw", "hourly"],
          options.MaximumHistoryPoints,
          options.MaximumHistoryEvents,
          options.MaximumHistoryDiagnostics,
          options.MaximumNodeHistoryPoints,
          options.MaximumNodeHistoryEvents,
          options.MaximumNodeHistoryDiagnostics,
          options.ConnectorPollSeconds,
          options.TelemetrySampleRetentionDays * 24,
          options.TelemetryRollupRetentionDays * 24);

  public static HistoryAppendPolicy CreateAppendPolicy(
      FleetDashboardOptions options) =>
      new(
          CreateRetention(options),
          TimeSpan.FromSeconds(options.HistoryClockSkewToleranceSeconds));
}
