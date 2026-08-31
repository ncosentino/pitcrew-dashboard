namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Describes one bounded campaign row without loading its complete target inventory.
/// </summary>
/// <param name="CampaignId">Campaign identifier.</param>
/// <param name="Kind">Forward or rollback campaign kind.</param>
/// <param name="SourceCampaignId">Source campaign for rollback.</param>
/// <param name="Candidate">Shared forward candidate authority.</param>
/// <param name="TargetSetHash">Immutable frozen target-set hash.</param>
/// <param name="Status">Current campaign lifecycle state.</param>
/// <param name="Revision">Monotonic mutation revision.</param>
/// <param name="WaveSize">Configured later-wave size.</param>
/// <param name="EligibleTargetCount">Frozen eligible target count.</param>
/// <param name="ExcludedTargetCount">Frozen excluded target count.</param>
/// <param name="CompleteTargetCount">Targets that proved full convergence.</param>
/// <param name="AdverseTargetCount">Failed, blocked, or indeterminate targets.</param>
/// <param name="CurrentWaveNumber">Active wave, when present.</param>
/// <param name="NextWaveNumber">Next pending wave, when present.</param>
/// <param name="RequestedByGitHubUserId">Campaign requester.</param>
/// <param name="RequestedAt">Dashboard creation time.</param>
/// <param name="ConfiguredAt">Canary and wave configuration time.</param>
/// <param name="CompletedAt">Campaign terminal time.</param>
public sealed record ImageRolloutCampaignSummary(
    Guid CampaignId,
    ImageRolloutCampaignKind Kind,
    Guid? SourceCampaignId,
    ImageRolloutCandidateAuthority? Candidate,
    string TargetSetHash,
    ImageRolloutCampaignStatus Status,
    int Revision,
    int? WaveSize,
    int EligibleTargetCount,
    int ExcludedTargetCount,
    int CompleteTargetCount,
    int AdverseTargetCount,
    int? CurrentWaveNumber,
    int? NextWaveNumber,
    string RequestedByGitHubUserId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ConfiguredAt,
    DateTimeOffset? CompletedAt);
