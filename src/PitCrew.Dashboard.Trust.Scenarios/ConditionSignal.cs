namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Represents one deterministic rule signal in the original incident decomposition.
/// </summary>
/// <param name="SignalId">Stable signal identifier.</param>
/// <param name="ConditionKey">Stable persistent-condition identity.</param>
/// <param name="ObservedAt">Time the source evidence was observed.</param>
/// <param name="EvaluatedAt">Time the rule produced the signal.</param>
/// <param name="Truth">Explicit true, false, or unknown signal state.</param>
public sealed record ConditionSignal(
    string SignalId,
    string ConditionKey,
    DateTimeOffset ObservedAt,
    DateTimeOffset EvaluatedAt,
    string Truth);
