namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Requests one frozen forward campaign draft from a ready candidate.
/// </summary>
/// <param name="CandidateId">Tenant-owned ready candidate identifier.</param>
public sealed record CreateImageRolloutCampaignRequest(Guid CandidateId);
