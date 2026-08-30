namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns a bounded campaign list with explicit truncation.
/// </summary>
/// <param name="Campaigns">Newest campaign summaries.</param>
/// <param name="Truncated">Whether additional campaigns were omitted by the bound.</param>
public sealed record ImageRolloutCampaignListResponse(
    IReadOnlyList<ImageRolloutCampaignSummaryResponse> Campaigns,
    bool Truncated);
