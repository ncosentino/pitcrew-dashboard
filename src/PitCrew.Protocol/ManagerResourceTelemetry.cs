using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one manager-produced point-in-time resource telemetry sample.
/// </summary>
/// <param name="SampledAt">Time the manager collected the resource sample.</param>
/// <param name="Status">Sample status: available, partial, or unavailable.</param>
/// <param name="Host">Host capacity when available; otherwise <see langword="null"/>.</param>
/// <param name="Manager">Manager process usage when available; otherwise <see langword="null"/>.</param>
public sealed record ManagerResourceTelemetry(
    [property: JsonRequired] DateTimeOffset SampledAt,
    [property: JsonRequired] string Status,
    [property: JsonRequired] HostResourceCapacity? Host,
    [property: JsonRequired] ResourceUsage? Manager);
