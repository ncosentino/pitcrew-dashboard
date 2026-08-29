using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Reports the locally observed terminal result of one profile-image rollout
/// command.
/// </summary>
/// <param name="CommandId">Command identifier supplied by the dashboard.</param>
/// <param name="Status">Terminal status: <c>succeeded</c>, <c>rejected</c>, <c>failed</c>, or <c>indeterminate</c>.</param>
/// <param name="FailureCategory">Bounded non-success category, or <see langword="null"/> after success.</param>
/// <param name="Message">Bounded operator-facing result detail.</param>
/// <param name="TargetDigest">Approved candidate digest that was applied on success, when known.</param>
/// <param name="TargetWorkerRevision">Worker revision after execution, when reported.</param>
/// <param name="ManagerConvergenceStatus">Manager convergence status observed after execution.</param>
/// <param name="CurrentWorkers">Live workers on the target revision, or <see langword="null"/> when unavailable.</param>
/// <param name="StaleWorkers">Live workers retained on the prior revision, or <see langword="null"/> when unavailable.</param>
/// <param name="LastError">Bounded most recent rollout error, or <see langword="null"/>.</param>
/// <param name="CompletedAt">Connector time when the command reached a terminal state.</param>
public sealed record ImageRolloutCommandOutcome(
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string Status,
    [property: JsonRequired] string? FailureCategory,
    [property: JsonRequired] string? Message,
    [property: JsonRequired] string? TargetDigest,
    [property: JsonRequired] string? TargetWorkerRevision,
    [property: JsonRequired] string ManagerConvergenceStatus,
    [property: JsonRequired] int? CurrentWorkers,
    [property: JsonRequired] int? StaleWorkers,
    [property: JsonRequired] string? LastError,
    [property: JsonRequired] DateTimeOffset CompletedAt);
