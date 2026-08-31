namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns one campaign wave and immutable approval evidence.
/// </summary>
/// <param name="WaveNumber">Immutable zero-based wave number.</param>
/// <param name="Status">Current wave lifecycle state.</param>
/// <param name="TargetCount">Frozen target count.</param>
/// <param name="ApprovedByGitHubUserId">Approving administrator.</param>
/// <param name="ApprovedAt">Dashboard approval time.</param>
/// <param name="CompletedAt">Wave terminal time.</param>
public sealed record ImageRolloutCampaignWaveResponse(
    int WaveNumber,
    string Status,
    int TargetCount,
    string? ApprovedByGitHubUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? CompletedAt);
