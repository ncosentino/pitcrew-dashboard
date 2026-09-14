using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Abstractions;

/// <summary>
/// Carries Dashboard receipt metadata and projected claims for one retained profile.
/// </summary>
/// <param name="ProfileId">Profile identifier local to the connector.</param>
/// <param name="DashboardReceivedAt">Time Dashboard accepted the stored profile revision.</param>
/// <param name="Claims">Claim-level evidence projected from the stored revision.</param>
public sealed record FleetProfileEvidence(
    string ProfileId,
    DateTimeOffset? DashboardReceivedAt,
    IReadOnlyList<EvidenceClaim> Claims);
