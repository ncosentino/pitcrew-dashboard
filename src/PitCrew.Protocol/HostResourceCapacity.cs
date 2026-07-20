using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes the host capacity used to interpret point-in-time resource usage.
/// </summary>
/// <param name="LogicalProcessorCount">Number of logical processors available on the host.</param>
/// <param name="MemoryBytes">Total host memory capacity in bytes.</param>
public sealed record HostResourceCapacity(
    [property: JsonRequired] int LogicalProcessorCount,
    [property: JsonRequired] long MemoryBytes);
