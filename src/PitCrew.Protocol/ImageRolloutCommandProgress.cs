using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Reports that one profile-image rollout command was durably claimed or
/// started locally.
/// </summary>
/// <param name="CommandId">Command identifier supplied by the dashboard.</param>
/// <param name="Phase">Durable local phase: <c>claimed</c> or <c>started</c>.</param>
/// <param name="ReportedAt">Connector time when the phase was durably recorded.</param>
public sealed record ImageRolloutCommandProgress(
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string Phase,
    [property: JsonRequired] DateTimeOffset ReportedAt);
