using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one manager contract 12 operation the manager performed against a subsystem.
/// </summary>
/// <param name="Operation">Closed-vocabulary operation name.</param>
/// <param name="ObservedAt">Time the manager observed the operation.</param>
/// <param name="DurationMilliseconds">Measured duration, or <see langword="null"/> when unmeasured.</param>
/// <param name="Reason">Manager-supplied reason vocabulary entry.</param>
/// <param name="Evidence">Sanitized operator-facing evidence, or <see langword="null"/> when the manager supplied none.</param>
public sealed record SubsystemOperationEvidence(
    [property: JsonRequired] string Operation,
    [property: JsonRequired] DateTimeOffset ObservedAt,
    [property: JsonRequired] int? DurationMilliseconds,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] string? Evidence);
