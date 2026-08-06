using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes the bounded GitHub Actions job currently owned by one autoscaled worker.
/// </summary>
public sealed record CurrentJobContext(
    [property: JsonRequired] string Repository,
    [property: JsonRequired] long WorkflowRunId,
    [property: JsonRequired] string JobId,
    [property: JsonRequired] string? DisplayName,
    [property: JsonRequired] string? EventName,
    [property: JsonRequired] DateTimeOffset? QueuedAt,
    [property: JsonRequired] DateTimeOffset? ScaleSetAssignedAt,
    [property: JsonRequired] DateTimeOffset? RunnerAssignedAt,
    [property: JsonRequired] DateTimeOffset StartedAt,
    [property: JsonRequired] DateTimeOffset? FinishedAt,
    [property: JsonRequired] string? Result);
