using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes aggregate pressure for the Docker engine host or VM visible to the manager.
/// </summary>
public sealed record HostPressureTelemetry(
    [property: JsonRequired] string Status,
    [property: JsonRequired] string Source,
    [property: JsonRequired] double? CpuUtilizationPercent,
    [property: JsonRequired] double? Load1,
    [property: JsonRequired] double? Load5,
    [property: JsonRequired] double? Load15,
    [property: JsonRequired] long? MemoryTotalBytes,
    [property: JsonRequired] long? MemoryAvailableBytes,
    [property: JsonRequired] long? SwapUsedBytes,
    [property: JsonRequired] double? CpuPressureSomeAvg10,
    [property: JsonRequired] double? CpuPressureFullAvg10,
    [property: JsonRequired] double? MemoryPressureSomeAvg10,
    [property: JsonRequired] double? MemoryPressureFullAvg10,
    [property: JsonRequired] double? IoPressureSomeAvg10,
    [property: JsonRequired] double? IoPressureFullAvg10);
