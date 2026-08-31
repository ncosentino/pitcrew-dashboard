namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Carries one leased campaign target into the existing profile command queue.
/// </summary>
/// <param name="CampaignId">Owning campaign identifier.</param>
/// <param name="TenantId">Tenant that owns the campaign and target.</param>
/// <param name="TargetId">Frozen campaign target identifier.</param>
/// <param name="NodeId">Dashboard node identifier.</param>
/// <param name="ProfileId">PitCrew profile identifier.</param>
/// <param name="WaveNumber">Approved immutable wave number.</param>
/// <param name="Candidate">Per-target image authority.</param>
/// <param name="Fences">Exact current profile fences.</param>
/// <param name="ApprovedByGitHubUserId">Administrator that approved the wave.</param>
/// <param name="IdempotencyKey">Stable target dispatch request key.</param>
public sealed record ImageRolloutCampaignDispatchClaim(
    Guid CampaignId,
    string TenantId,
    Guid TargetId,
    Guid NodeId,
    string ProfileId,
    int WaveNumber,
    ImageRolloutCandidateAuthority Candidate,
    ImageRolloutCommandFences Fences,
    string ApprovedByGitHubUserId,
    string IdempotencyKey);
