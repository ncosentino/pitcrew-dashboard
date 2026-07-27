using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Summarizes manager contract 12 health for the operations one manager performed against a subsystem.
/// </summary>
/// <remarks>
/// These summaries describe operations this manager performed. They never claim that the host, the
/// Docker daemon, the network, or the GitHub service as a whole is healthy or unhealthy.
/// </remarks>
/// <param name="State">Manager-reported state: healthy, degraded, unavailable, or unknown.</param>
/// <param name="ObservedAt">Time the manager published this summary.</param>
/// <param name="ConsecutiveFailures">Consecutive failed operations against the subsystem.</param>
/// <param name="RetryAt">Scheduled retry time while backing off; otherwise <see langword="null"/>.</param>
/// <param name="LastSuccess">Most recent successful operation, or <see langword="null"/> when none was observed.</param>
/// <param name="LastFailure">Most recent failed operation, or <see langword="null"/> when none was observed.</param>
public sealed record SubsystemHealthSummary(
    [property: JsonRequired] string State,
    [property: JsonRequired] DateTimeOffset ObservedAt,
    [property: JsonRequired] int ConsecutiveFailures,
    [property: JsonRequired] DateTimeOffset? RetryAt,
    [property: JsonRequired] SubsystemOperationEvidence? LastSuccess,
    [property: JsonRequired] SubsystemOperationEvidence? LastFailure);
