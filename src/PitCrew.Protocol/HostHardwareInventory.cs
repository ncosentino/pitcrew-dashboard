using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes sanitized Docker-host hardware and runtime inventory.
/// </summary>
/// <param name="Status">Collection status: current, stale, or unavailable.</param>
/// <param name="CollectedAt">Time the current inventory hash was first observed.</param>
/// <param name="AttemptedAt">Time of the latest bounded collection attempt.</param>
/// <param name="InventoryHash">SHA-256 hash of the ordered hardware values.</param>
/// <param name="ProcessorModel">Sanitized processor model when observable.</param>
/// <param name="Architecture">Canonical processor architecture when observable.</param>
/// <param name="PhysicalCoreCount">Physical core count when topology is complete.</param>
/// <param name="LogicalProcessorCount">Docker-visible logical processor count.</param>
/// <param name="PerformanceCoreCount">Performance-core count when reliably observable.</param>
/// <param name="EfficiencyCoreCount">Efficiency-core count when reliably observable.</param>
/// <param name="MemoryBytes">Docker-visible host memory in bytes.</param>
/// <param name="OperatingSystem">Sanitized Docker operating-system identity.</param>
/// <param name="KernelVersion">Sanitized kernel version.</param>
/// <param name="DockerServerVersion">Docker server version.</param>
/// <param name="DockerStorageDriver">Docker storage driver.</param>
/// <param name="DockerBackingFilesystem">Docker backing filesystem when reported.</param>
public sealed record HostHardwareInventory(
    [property: JsonRequired] string Status,
    [property: JsonRequired] DateTimeOffset? CollectedAt,
    [property: JsonRequired] DateTimeOffset AttemptedAt,
    [property: JsonRequired] string? InventoryHash,
    [property: JsonRequired] string? ProcessorModel,
    [property: JsonRequired] string? Architecture,
    [property: JsonRequired] long? PhysicalCoreCount,
    [property: JsonRequired] long? LogicalProcessorCount,
    [property: JsonRequired] long? PerformanceCoreCount,
    [property: JsonRequired] long? EfficiencyCoreCount,
    [property: JsonRequired] long? MemoryBytes,
    [property: JsonRequired] string? OperatingSystem,
    [property: JsonRequired] string? KernelVersion,
    [property: JsonRequired] string? DockerServerVersion,
    [property: JsonRequired] string? DockerStorageDriver,
    [property: JsonRequired] string? DockerBackingFilesystem);

/// <summary>
/// Groups node-level observations published by one profile manager.
/// </summary>
/// <param name="Hardware">Sanitized host hardware inventory.</param>
public sealed record ObservedHost(
    [property: JsonRequired] HostHardwareInventory Hardware);
