using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Abstractions;

/// <summary>
/// Represents one server and its current profile projections in a fleet response.
/// </summary>
/// <param name="NodeId">Dashboard-assigned node identifier.</param>
/// <param name="DisplayName">Operator-facing server name.</param>
/// <param name="ConnectorVersion">Most recently observed connector application version.</param>
/// <param name="EnrolledAt">Time the connector was first enrolled.</param>
/// <param name="LastSeenAt">Time the dashboard last accepted a connector synchronization.</param>
/// <param name="IsOnline">Whether the node is within the dashboard freshness window.</param>
/// <param name="IsRevoked">Whether the node credential has been revoked.</param>
/// <param name="CredentialRotationRequested">Whether an administrator requested credential rotation.</param>
/// <param name="Profiles">Latest profile projections for the node.</param>
public sealed record FleetNode(
    Guid NodeId,
    string DisplayName,
    string ConnectorVersion,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? LastSeenAt,
    bool IsOnline,
    bool IsRevoked,
    bool CredentialRotationRequested,
    IReadOnlyList<ManagerObservedState> Profiles);

/// <summary>
/// Returns the current fleet projection visible to one dashboard tenant.
/// </summary>
/// <param name="GeneratedAt">Dashboard time when the response was generated.</param>
/// <param name="Nodes">Enrolled nodes and their latest profiles.</param>
public sealed record FleetResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<FleetNode> Nodes);
