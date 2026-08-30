namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns bounded candidate authority used by a campaign or target.
/// </summary>
/// <param name="CandidateId">Immutable candidate identifier.</param>
/// <param name="RecipeId">Locally mapped recipe identifier.</param>
/// <param name="TargetDigest">Immutable target digest.</param>
/// <param name="TargetPlatform">Closed Linux target platform.</param>
public sealed record ImageRolloutCampaignCandidateResponse(
    Guid CandidateId,
    string RecipeId,
    string TargetDigest,
    string TargetPlatform);
