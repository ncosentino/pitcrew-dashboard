using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes the latest bounded admission decision reported for one profile.
/// </summary>
/// <param name="Sequence">Coordinator decision sequence associated with the operation.</param>
/// <param name="Command">Lease operation that produced the decision.</param>
/// <param name="Granted">Whether the operation succeeded.</param>
/// <param name="FailureCategory">Bounded coordinator error code for a rejected operation.</param>
/// <param name="DecidedAtUnixNano">UTC Unix timestamp in nanoseconds supplied by the coordinator.</param>
public sealed record HostAdmissionDecision(
    [property: JsonRequired] long Sequence,
    [property: JsonRequired] string Command,
    [property: JsonRequired] bool Granted,
    [property: JsonRequired] string? FailureCategory,
    [property: JsonRequired] long DecidedAtUnixNano);
