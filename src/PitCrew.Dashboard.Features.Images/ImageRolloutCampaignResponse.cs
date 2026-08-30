namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns one complete campaign with frozen targets and wave evidence.
/// </summary>
/// <param name="CampaignId">Campaign identifier.</param>
/// <param name="Kind">Forward or rollback campaign kind.</param>
/// <param name="SourceCampaignId">Source campaign for rollback.</param>
/// <param name="Candidate">Shared forward candidate authority.</param>
/// <param name="TargetSetHash">Immutable frozen target-set hash.</param>
/// <param name="Status">Current campaign lifecycle state.</param>
/// <param name="Revision">Monotonic campaign revision.</param>
/// <param name="WaveSize">Configured later-wave size.</param>
/// <param name="RequestedByGitHubUserId">Campaign requester.</param>
/// <param name="RequestedAt">Campaign creation time.</param>
/// <param name="ConfiguredByGitHubUserId">Administrator that configured canary and waves.</param>
/// <param name="ConfiguredAt">Canary and wave configuration time.</param>
/// <param name="PausedAt">Most recent pause time.</param>
/// <param name="CancelledAt">Cancellation time.</param>
/// <param name="CompletedAt">Campaign terminal time.</param>
/// <param name="Targets">Complete frozen eligible and excluded target inventory.</param>
/// <param name="Waves">Immutable wave and approval evidence.</param>
public sealed record ImageRolloutCampaignResponse(
    Guid CampaignId,
    string Kind,
    Guid? SourceCampaignId,
    ImageRolloutCampaignCandidateResponse? Candidate,
    string TargetSetHash,
    string Status,
    int Revision,
    int? WaveSize,
    string RequestedByGitHubUserId,
    DateTimeOffset RequestedAt,
    string? ConfiguredByGitHubUserId,
    DateTimeOffset? ConfiguredAt,
    DateTimeOffset? PausedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ImageRolloutCampaignTargetResponse> Targets,
    IReadOnlyList<ImageRolloutCampaignWaveResponse> Waves);
