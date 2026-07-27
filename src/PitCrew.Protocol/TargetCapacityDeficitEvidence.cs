using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes manager contract 12 capacity-deficit evidence for one autoscaling target.
/// </summary>
/// <param name="Key">Scale-set target key that already appears in observed state.</param>
/// <param name="Repository">Sanitized repository identity, or <see langword="null"/> for shared scopes.</param>
/// <param name="ObservedAt">Time the manager measured the capacity evidence.</param>
/// <param name="Freshness">Evidence freshness: current, stale, or unavailable.</param>
/// <param name="TargetSlots">Accepted activation target for this scale-set target.</param>
/// <param name="ActiveWorkers">Workers the manager observes as active.</param>
/// <param name="StartingWorkers">Workers the manager observes as starting.</param>
/// <param name="DrainingWorkers">Workers the manager observes as draining.</param>
/// <param name="CleanupPendingWorkers">Workers awaiting manager cleanup.</param>
/// <param name="EligibleWorkers">Control-plane eligible workers, or <see langword="null"/> when unavailable.</param>
/// <param name="LocalDeficit">Manager-reported shortfall against the activation target.</param>
/// <param name="EligibilityDeficit">Manager-reported eligibility shortfall, or <see langword="null"/> when eligibility is unavailable.</param>
/// <param name="Reason">Manager-supplied blocking reason vocabulary entry.</param>
/// <param name="Evidence">Sanitized operator-facing evidence, or <see langword="null"/> when the manager supplied none.</param>
public sealed record TargetCapacityDeficitEvidence(
    [property: JsonRequired] string Key,
    [property: JsonRequired] string? Repository,
    DateTimeOffset ObservedAt,
    string Freshness,
    int TargetSlots,
    int ActiveWorkers,
    int StartingWorkers,
    int DrainingWorkers,
    int CleanupPendingWorkers,
    int? EligibleWorkers,
    int LocalDeficit,
    int? EligibilityDeficit,
    string Reason,
    string? Evidence) : CapacityDeficitEvidence(
        ObservedAt,
        Freshness,
        TargetSlots,
        ActiveWorkers,
        StartingWorkers,
        DrainingWorkers,
        CleanupPendingWorkers,
        EligibleWorkers,
        LocalDeficit,
        EligibilityDeficit,
        Reason,
        Evidence);
