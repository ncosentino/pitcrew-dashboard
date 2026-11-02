namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Returns Dashboard-derived configuration and runtime status for one exact profile.
/// </summary>
/// <param name="GeneratedAt">Dashboard time when the status was evaluated.</param>
/// <param name="NodeId">Dashboard-assigned node identifier.</param>
/// <param name="ProfileId">Requested profile identifier.</param>
/// <param name="InventoryCoverage">Connector inventory coverage: complete, partial, or unavailable.</param>
/// <param name="InventoryAuthoritative">Whether current complete inventory can prove profile absence.</param>
/// <param name="InventoryObservedAt">Connector time when profile inventory was acquired.</param>
/// <param name="InventoryReceivedAt">Dashboard time when profile inventory was accepted.</param>
/// <param name="InventoryUnavailableReason">Closed connector acquisition reason when inventory is incomplete.</param>
/// <param name="ConfigurationState">Configured, not-configured, or unknown.</param>
/// <param name="RuntimeState">Current runtime state when configuration is known to exist.</param>
/// <param name="ReasonCode">Stable machine-readable reason for the classification.</param>
/// <param name="Summary">Concise operator-facing classification banner.</param>
/// <param name="NodeOnline">Whether the connector node is within its freshness window.</param>
/// <param name="NodeLastSeenAt">Dashboard time when the connector last synchronized.</param>
/// <param name="ManagerStatus">Manager lifecycle status when the profile exists.</param>
/// <param name="ManagerObservedAt">Manager source time for the current profile projection.</param>
/// <param name="CapacityMode">Autoscaled or fixed when the profile exists.</param>
/// <param name="ExplicitlyPaused">Whether Dashboard has acknowledged a fenced zero-capacity pause.</param>
/// <param name="ConfiguredMaximum">Current configured maximum when reported.</param>
/// <param name="MinimumIdleSlots">Autoscaling minimum idle slots when reported.</param>
/// <param name="TargetSlots">Current activation target when reported.</param>
/// <param name="ActiveSlots">Current active local slots when reported.</param>
/// <param name="IdleRunners">Current autoscaling idle runner count when reported.</param>
/// <param name="BusyRunners">Current autoscaling busy runner count when reported.</param>
/// <param name="DrainingSlots">Current draining local slot count when reported.</param>
/// <param name="AutoscalingStatus">Manager autoscaling lifecycle status when reported.</param>
public sealed record ProfileOperationalStatusResponse(
    DateTimeOffset GeneratedAt,
    Guid NodeId,
    string ProfileId,
    string InventoryCoverage,
    bool InventoryAuthoritative,
    DateTimeOffset? InventoryObservedAt,
    DateTimeOffset? InventoryReceivedAt,
    string? InventoryUnavailableReason,
    string ConfigurationState,
    string? RuntimeState,
    string ReasonCode,
    string Summary,
    bool NodeOnline,
    DateTimeOffset? NodeLastSeenAt,
    string? ManagerStatus,
    DateTimeOffset? ManagerObservedAt,
    string? CapacityMode,
    bool? ExplicitlyPaused,
    int? ConfiguredMaximum,
    int? MinimumIdleSlots,
    int? TargetSlots,
    int? ActiveSlots,
    int? IdleRunners,
    int? BusyRunners,
    int? DrainingSlots,
    string? AutoscalingStatus);
