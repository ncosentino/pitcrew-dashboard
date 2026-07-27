using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes manager contract 12 evidence for missing capacity against one activation target.
/// </summary>
/// <remarks>
/// Every value is observed manager state rather than a cause inferred by a dashboard.
/// <paramref name="TargetSlots"/> is the accepted activation target and is never the configured
/// autoscaling maximum, so a configured ceiling never creates a deficit by itself. A
/// <see langword="null"/> <paramref name="EligibleWorkers"/> means the manager has no current
/// control-plane evidence, while zero means it observed none.
/// </remarks>
/// <param name="ObservedAt">Time the manager measured the capacity evidence.</param>
/// <param name="Freshness">Evidence freshness: current, stale, or unavailable.</param>
/// <param name="TargetSlots">Accepted activation target the manager is reconciling toward.</param>
/// <param name="ActiveWorkers">Workers the manager observes as active.</param>
/// <param name="StartingWorkers">Workers the manager observes as starting.</param>
/// <param name="DrainingWorkers">Workers the manager observes as draining.</param>
/// <param name="CleanupPendingWorkers">Workers awaiting manager cleanup.</param>
/// <param name="EligibleWorkers">Control-plane eligible workers, or <see langword="null"/> when unavailable.</param>
/// <param name="LocalDeficit">Manager-reported shortfall against the activation target.</param>
/// <param name="EligibilityDeficit">Manager-reported eligibility shortfall, or <see langword="null"/> when eligibility is unavailable.</param>
/// <param name="Reason">Manager-supplied blocking reason vocabulary entry.</param>
/// <param name="Evidence">Sanitized operator-facing evidence, or <see langword="null"/> when the manager supplied none.</param>
public record CapacityDeficitEvidence(
    [property: JsonRequired] DateTimeOffset ObservedAt,
    [property: JsonRequired] string Freshness,
    [property: JsonRequired] int TargetSlots,
    [property: JsonRequired] int ActiveWorkers,
    [property: JsonRequired] int StartingWorkers,
    [property: JsonRequired] int DrainingWorkers,
    [property: JsonRequired] int CleanupPendingWorkers,
    [property: JsonRequired] int? EligibleWorkers,
    [property: JsonRequired] int LocalDeficit,
    [property: JsonRequired] int? EligibilityDeficit,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] string? Evidence);
