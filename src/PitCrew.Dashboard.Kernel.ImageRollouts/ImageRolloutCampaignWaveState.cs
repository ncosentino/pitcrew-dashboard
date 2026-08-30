namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Describes one immutable campaign wave and its approval evidence.
/// </summary>
/// <param name="WaveNumber">Immutable zero-based wave number.</param>
/// <param name="Status">Current wave lifecycle state.</param>
/// <param name="TargetCount">Frozen targets assigned to the wave.</param>
/// <param name="ApprovedByGitHubUserId">Approving administrator, when approved.</param>
/// <param name="ApprovedAt">Dashboard approval time, when approved.</param>
/// <param name="CompletedAt">Dashboard terminal time, when terminal.</param>
public sealed record ImageRolloutCampaignWaveState(
    int WaveNumber,
    ImageRolloutCampaignWaveStatus Status,
    int TargetCount,
    string? ApprovedByGitHubUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? CompletedAt);
