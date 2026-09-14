namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Represents one deterministic incident before any future grouping.
/// </summary>
/// <param name="IncidentId">Stable synthetic incident identifier.</param>
/// <param name="ConditionKey">Distinct source condition identity.</param>
/// <param name="Sequence">One-based deterministic sequence.</param>
public sealed record ScenarioIncident(
    string IncidentId,
    string ConditionKey,
    int Sequence);
