namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Keeps source, receipt, evaluation, and response clocks separate for one scenario.
/// </summary>
/// <param name="SourceObservedAt">Time the source observed the evidence.</param>
/// <param name="DashboardReceivedAt">Time Dashboard accepted the evidence.</param>
/// <param name="EvaluatedAt">Time Dashboard evaluated the evidence.</param>
/// <param name="ResponseGeneratedAt">Time Dashboard generated the response.</param>
public sealed record ScenarioClocks(
    DateTimeOffset SourceObservedAt,
    DateTimeOffset DashboardReceivedAt,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset ResponseGeneratedAt);
