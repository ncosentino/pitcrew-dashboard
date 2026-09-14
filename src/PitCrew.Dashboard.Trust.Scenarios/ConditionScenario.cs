namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Distinguishes repeated signals for one condition from independent conditions.
/// </summary>
/// <param name="Id">Stable scenario identifier.</param>
/// <param name="Signals">Original ungrouped signal evidence.</param>
/// <param name="ExpectedConditionCount">Number of persistent conditions represented.</param>
/// <param name="ExpectedIncidentCount">Current one-condition-per-incident decomposition.</param>
public sealed record ConditionScenario(
    string Id,
    IReadOnlyList<ConditionSignal> Signals,
    int ExpectedConditionCount,
    int ExpectedIncidentCount);
