using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one timestamped GitHub scale-set statistic sample for a manager contract 11 target.
/// </summary>
/// <remarks>
/// These counts are external GitHub evidence observed at <paramref name="ObservedAt"/>. They never
/// describe local worker containers and never substitute for local worker counts.
/// </remarks>
/// <param name="ObservedAt">Time GitHub reported the statistics.</param>
/// <param name="AvailableJobs">Jobs GitHub reports as available for acquisition.</param>
/// <param name="AcquiredJobs">Jobs GitHub reports as acquired by the scale set.</param>
/// <param name="AssignedJobs">Jobs GitHub reports as assigned to the scale set.</param>
/// <param name="RunningJobs">Assigned jobs GitHub reports as running.</param>
/// <param name="RegisteredRunners">Runners GitHub reports as registered for the scale set.</param>
/// <param name="BusyRunners">Registered runners GitHub reports as busy.</param>
/// <param name="IdleRunners">Registered runners GitHub reports as idle.</param>
public sealed record ScaleSetStatistics(
    [property: JsonRequired] DateTimeOffset ObservedAt,
    [property: JsonRequired] int AvailableJobs,
    [property: JsonRequired] int AcquiredJobs,
    [property: JsonRequired] int AssignedJobs,
    [property: JsonRequired] int RunningJobs,
    [property: JsonRequired] int RegisteredRunners,
    [property: JsonRequired] int BusyRunners,
    [property: JsonRequired] int IdleRunners);
