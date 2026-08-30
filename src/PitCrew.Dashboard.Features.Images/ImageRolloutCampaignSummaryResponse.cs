namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns one bounded campaign list row.
/// </summary>
/// <param name="CampaignId">Campaign identifier.</param>
/// <param name="Kind">Forward or rollback campaign kind.</param>
/// <param name="SourceCampaignId">Source campaign for rollback.</param>
/// <param name="Candidate">Shared forward candidate authority.</param>
/// <param name="TargetSetHash">Immutable frozen target-set hash.</param>
/// <param name="Status">Current campaign lifecycle state.</param>
/// <param name="Revision">Monotonic campaign revision.</param>
/// <param name="WaveSize">Configured later-wave size.</param>
/// <param name="EligibleTargetCount">Frozen eligible target count.</param>
/// <param name="ExcludedTargetCount">Frozen excluded target count.</param>
/// <param name="CompleteTargetCount">Targets that proved full convergence.</param>
/// <param name="AdverseTargetCount">Failed, blocked, or indeterminate targets.</param>
/// <param name="CurrentWaveNumber">Active wave number.</param>
/// <param name="NextWaveNumber">Next pending wave number.</param>
/// <param name="RequestedByGitHubUserId">Campaign requester.</param>
/// <param name="RequestedAt">Campaign creation time.</param>
/// <param name="ConfiguredAt">Canary and wave configuration time.</param>
/// <param name="CompletedAt">Campaign terminal time.</param>
public sealed record ImageRolloutCampaignSummaryResponse(
    Guid CampaignId,
    string Kind,
    Guid? SourceCampaignId,
    ImageRolloutCampaignCandidateResponse? Candidate,
    string TargetSetHash,
    string Status,
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
