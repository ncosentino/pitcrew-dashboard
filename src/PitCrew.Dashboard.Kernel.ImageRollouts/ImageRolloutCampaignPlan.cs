namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Carries one complete frozen campaign plan into durable persistence.
/// </summary>
/// <param name="CampaignId">Dashboard-assigned campaign identifier.</param>
/// <param name="TenantId">Tenant that owns the campaign.</param>
/// <param name="Kind">Forward or explicit rollback campaign kind.</param>
/// <param name="SourceCampaignId">Source campaign for rollback, otherwise <see langword="null"/>.</param>
/// <param name="Candidate">Shared forward candidate authority, otherwise <see langword="null"/>.</param>
/// <param name="TargetSetHash">Deterministic hash of candidate and target authority.</param>
/// <param name="RequestedByGitHubUserId">Authenticated campaign requester.</param>
/// <param name="RequestedAt">Dashboard request time.</param>
/// <param name="Targets">Complete eligible and excluded frozen target inventory.</param>
/// <param name="IdempotencyKey">Stable request key.</param>
/// <param name="IdempotencySignature">Hash of the creation authority.</param>
public sealed record ImageRolloutCampaignPlan(
    Guid CampaignId,
    string TenantId,
    ImageRolloutCampaignKind Kind,
    Guid? SourceCampaignId,
    ImageRolloutCandidateAuthority? Candidate,
    string TargetSetHash,
    string RequestedByGitHubUserId,
    DateTimeOffset RequestedAt,
    IReadOnlyList<ImageRolloutCampaignPlannedTarget> Targets,
    string IdempotencyKey,
    string IdempotencySignature);
