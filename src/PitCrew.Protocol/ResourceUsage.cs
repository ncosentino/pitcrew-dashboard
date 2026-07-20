using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one point-in-time process or worker resource-usage sample.
/// </summary>
/// <param name="CpuCores">CPU consumption expressed as cores, without a utilization-percentage cap.</param>
/// <param name="MemoryWorkingSetBytes">Current memory working set in bytes.</param>
/// <param name="Pids">Current process identifier count.</param>
public sealed record ResourceUsage(
    [property: JsonRequired] double CpuCores,
    [property: JsonRequired] long MemoryWorkingSetBytes,
    [property: JsonRequired] int Pids);
