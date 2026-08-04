using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Returns one bounded page of current fleet diagnostics.
/// </summary>
/// <param name="GeneratedAt">Dashboard time when the fleet was read.</param>
/// <param name="Nodes">Scoped nodes ordered by identifier.</param>
/// <param name="NextAfterNodeId">Cursor for the next page, or <see langword="null"/>.</param>
public sealed record DiagnosticFleetPageResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<FleetNode> Nodes,
    Guid? NextAfterNodeId);
