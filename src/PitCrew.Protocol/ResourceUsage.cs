using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one point-in-time process or worker resource-usage sample.
/// </summary>
/// <remarks>
/// Manager contract 11 adds cumulative network and block-I/O counters. Each counter is
/// independently optional: <see langword="null"/> means the measurement is unavailable, while
/// zero means it was measured as zero.
/// </remarks>
/// <param name="CpuCores">CPU consumption expressed as cores, without a utilization-percentage cap.</param>
/// <param name="MemoryWorkingSetBytes">Current memory working set in bytes.</param>
/// <param name="Pids">Current process identifier count.</param>
/// <param name="NetworkRxBytes">Cumulative received network bytes, or <see langword="null"/> when unavailable.</param>
/// <param name="NetworkTxBytes">Cumulative transmitted network bytes, or <see langword="null"/> when unavailable.</param>
/// <param name="BlockReadBytes">Cumulative block-device bytes read, or <see langword="null"/> when unavailable.</param>
/// <param name="BlockWriteBytes">Cumulative block-device bytes written, or <see langword="null"/> when unavailable.</param>
public sealed record ResourceUsage(
    [property: JsonRequired] double CpuCores,
    [property: JsonRequired] long MemoryWorkingSetBytes,
    [property: JsonRequired] int Pids,
    long? NetworkRxBytes = null,
    long? NetworkTxBytes = null,
    long? BlockReadBytes = null,
    long? BlockWriteBytes = null);
