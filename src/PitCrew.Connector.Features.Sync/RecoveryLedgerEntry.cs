namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Records one durable local recovery attempt.
/// </summary>
/// <param name="CommandId">Dashboard-assigned at-most-once command identifier.</param>
/// <param name="ProfileId">Locally resolved profile identifier.</param>
/// <param name="ExpectedManagerInstanceId">Fenced manager instance supplied by the dashboard.</param>
/// <param name="ExpectedGeneration">Fenced desired-capacity generation.</param>
/// <param name="ExpectedDesiredStateHash">Fenced desired-state hash, or <see langword="null"/>.</param>
/// <param name="ResolvedManagerInstanceId">Manager instance locally resolved before execution.</param>
/// <param name="ResolvedGeneration">Generation locally resolved before execution.</param>
/// <param name="ResolvedDesiredStateHash">Desired-state hash locally resolved before execution.</param>
/// <param name="StartedAt">Connector time when the attempt was durably recorded.</param>
/// <param name="Phase">Durable phase: started or terminal.</param>
/// <param name="Status">Terminal status when the attempt is resolved; otherwise <see langword="null"/>.</param>
/// <param name="FailureCategory">Bounded non-success category, or <see langword="null"/>.</param>
/// <param name="Message">Bounded operator-facing result detail.</param>
/// <param name="AfterManagerInstanceId">Manager instance observed after execution.</param>
/// <param name="CompletedAt">Connector time when the attempt reached a terminal state.</param>
internal sealed record RecoveryLedgerEntry(
    Guid CommandId,
    string ProfileId,
    string ExpectedManagerInstanceId,
    int ExpectedGeneration,
    string? ExpectedDesiredStateHash,
    string ResolvedManagerInstanceId,
    int ResolvedGeneration,
    string? ResolvedDesiredStateHash,
    DateTimeOffset StartedAt,
    string Phase,
    string? Status,
    string? FailureCategory,
    string? Message,
    string? AfterManagerInstanceId,
    DateTimeOffset? CompletedAt);
