using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one durable manager contract 12 operation event.
/// </summary>
/// <remarks>
/// <paramref name="Sequence"/> is durable and monotonic across manager restart, so an event is
/// identified by its profile and sequence rather than by the observing manager instance or by the
/// connector heartbeat that carried it.
/// </remarks>
/// <param name="Sequence">Durable monotonic identity of the event within its profile.</param>
/// <param name="ManagerInstanceId">Manager instance that observed the operation.</param>
/// <param name="ObservedAt">Time the manager observed the operation.</param>
/// <param name="Subsystem">Manager subsystem that performed the operation.</param>
/// <param name="Operation">Closed-vocabulary operation name.</param>
/// <param name="Target">Slot or scale-set target key already present in observed state, or <see langword="null"/>.</param>
/// <param name="Outcome">Manager-reported outcome of the operation.</param>
/// <param name="DurationMilliseconds">Measured duration, or <see langword="null"/> when unmeasured; zero means a measured sub-millisecond operation.</param>
/// <param name="Attempt">Attempt number when reported; otherwise <see langword="null"/>.</param>
/// <param name="ConsecutiveFailures">Consecutive failures when reported; otherwise <see langword="null"/>.</param>
/// <param name="RetryAt">Scheduled retry time when a retry is pending; otherwise <see langword="null"/>.</param>
/// <param name="Reason">Manager-supplied reason vocabulary entry.</param>
/// <param name="Evidence">Sanitized operator-facing evidence, or <see langword="null"/> when the manager supplied none.</param>
public sealed record ManagerEvent(
    [property: JsonRequired] long Sequence,
    [property: JsonRequired] string ManagerInstanceId,
    [property: JsonRequired] DateTimeOffset ObservedAt,
    [property: JsonRequired] string Subsystem,
    [property: JsonRequired] string Operation,
    [property: JsonRequired] string? Target,
    [property: JsonRequired] string Outcome,
    [property: JsonRequired] int? DurationMilliseconds,
    [property: JsonRequired] int? Attempt,
    [property: JsonRequired] int? ConsecutiveFailures,
    [property: JsonRequired] DateTimeOffset? RetryAt,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] string? Evidence);
