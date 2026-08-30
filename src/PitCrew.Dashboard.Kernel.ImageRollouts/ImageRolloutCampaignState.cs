namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Describes one complete tenant-scoped campaign with frozen targets and waves.
/// </summary>
/// <param name="CampaignId">Campaign identifier.</param>
/// <param name="TenantId">Owning tenant identifier.</param>
/// <param name="Kind">Forward or rollback campaign kind.</param>
/// <param name="SourceCampaignId">Source campaign for rollback.</param>
/// <param name="Candidate">Shared forward candidate authority.</param>
/// <param name="TargetSetHash">Immutable frozen target-set hash.</param>
/// <param name="Status">Current campaign lifecycle state.</param>
/// <param name="Revision">Monotonic mutation revision.</param>
/// <param name="WaveSize">Configured later-wave size.</param>
/// <param name="RequestedByGitHubUserId">Campaign requester.</param>
/// <param name="RequestedAt">Dashboard creation time.</param>
/// <param name="ConfiguredByGitHubUserId">Administrator that configured canary and waves.</param>
/// <param name="ConfiguredAt">Canary and wave configuration time.</param>
/// <param name="PausedAt">Most recent pause time.</param>
/// <param name="CancelledAt">Cancellation time.</param>
/// <param name="CompletedAt">Campaign terminal time.</param>
/// <param name="Targets">Complete frozen eligible and excluded target inventory.</param>
/// <param name="Waves">Immutable wave and approval evidence.</param>
public sealed record ImageRolloutCampaignState(
    Guid CampaignId,
    string TenantId,
    ImageRolloutCampaignKind Kind,
    Guid? SourceCampaignId,
    ImageRolloutCandidateAuthority? Candidate,
    string TargetSetHash,
    ImageRolloutCampaignStatus Status,
    int Revision,
    int? WaveSize,
    string RequestedByGitHubUserId,
    DateTimeOffset RequestedAt,
    string? ConfiguredByGitHubUserId,
    DateTimeOffset? ConfiguredAt,
    DateTimeOffset? PausedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ImageRolloutCampaignTargetState> Targets,
    IReadOnlyList<ImageRolloutCampaignWaveState> Waves);
