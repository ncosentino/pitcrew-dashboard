using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one manager contract 11 scale-set target and its separate local and GitHub evidence.
/// </summary>
/// <remarks>
/// The <c>local*Workers</c> counts describe local Docker containers observed by the manager, while
/// <paramref name="Statistics"/> carries timestamped GitHub evidence. The two sources are never
/// collapsed, and a mismatch between them never proves that a container is eligible or that a
/// registration is safe to remove.
/// </remarks>
/// <param name="Key">Stable manager-assigned target key.</param>
/// <param name="Repository">Sanitized repository identity, or <see langword="null"/> for shared scopes.</param>
/// <param name="MaximumSlots">Configured upper bound for this target.</param>
/// <param name="TargetSlots">Current activation target for this target.</param>
/// <param name="LocalActiveWorkers">Local worker containers the manager still owns for this target.</param>
/// <param name="LocalIdleWorkers">Local worker containers the manager observes as idle.</param>
/// <param name="LocalBusyWorkers">Local worker containers the manager observes as busy.</param>
/// <param name="LocalDrainingWorkers">Local worker containers the manager observes as draining.</param>
/// <param name="Statistics">Timestamped GitHub statistics, or <see langword="null"/> when unavailable.</param>
public sealed record AutoscalingTargetState(
    [property: JsonRequired] string Key,
    [property: JsonRequired] string? Repository,
    [property: JsonRequired] int MaximumSlots,
    [property: JsonRequired] int TargetSlots,
    [property: JsonRequired] int LocalActiveWorkers,
    [property: JsonRequired] int LocalIdleWorkers,
    [property: JsonRequired] int LocalBusyWorkers,
    [property: JsonRequired] int LocalDrainingWorkers,
    [property: JsonRequired] ScaleSetStatistics? Statistics);
