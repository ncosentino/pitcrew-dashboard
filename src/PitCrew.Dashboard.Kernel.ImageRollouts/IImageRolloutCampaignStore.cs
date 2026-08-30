namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Persists frozen image rollout campaigns and restart-safe target dispatch state.
/// </summary>
public interface IImageRolloutCampaignStore
{
  /// <summary>
  /// Persists one complete frozen forward or rollback campaign plan.
  /// </summary>
  Task<ImageRolloutCampaignMutation> CreateAsync(
      ImageRolloutCampaignPlan plan,
      int maximumTargets,
      CancellationToken cancellationToken);

  /// <summary>
  /// Freezes canary and wave assignment for one draft campaign.
  /// </summary>
  Task<ImageRolloutCampaignMutation> ConfigureAsync(
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignConfiguration configuration,
      string actorGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset configuredAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Approves one exact pending wave.
  /// </summary>
  Task<ImageRolloutCampaignMutation> ApproveWaveAsync(
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignWaveApproval approval,
      string actorGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset approvedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Pauses future target dispatch.
  /// </summary>
  Task<ImageRolloutCampaignMutation> PauseAsync(
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string actorGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset pausedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Resumes future target dispatch or returns to the next approval gate.
  /// </summary>
  Task<ImageRolloutCampaignMutation> ResumeAsync(
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string actorGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset resumedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Cancels every target that does not yet have a durable profile command.
  /// </summary>
  Task<ImageRolloutCampaignMutation> CancelAsync(
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string actorGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset cancelledAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads newest campaign summaries for one tenant.
  /// </summary>
  Task<IReadOnlyList<ImageRolloutCampaignSummary>> ListAsync(
      string tenantId,
      int limit,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads one complete campaign by tenant and identifier.
  /// </summary>
  Task<ImageRolloutCampaignState?> GetOrNullAsync(
      string tenantId,
      Guid campaignId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Reconciles linked profile commands and current capability into campaign target state.
  /// </summary>
  Task ReconcileAsync(
      DateTimeOffset observedAt,
      int observedStateMaximumAgeSeconds,
      int maximumCampaigns,
      CancellationToken cancellationToken);

  /// <summary>
  /// Leases due approved targets within campaign and node concurrency limits.
  /// </summary>
  Task<IReadOnlyList<ImageRolloutCampaignDispatchClaim>> ClaimDueTargetsAsync(
      string leaseOwner,
      DateTimeOffset claimedAt,
      DateTimeOffset leaseExpiresAt,
      int maximumClaims,
      int maximumConcurrentTargetsPerCampaign,
      int maximumConcurrentTargetsPerNode,
      CancellationToken cancellationToken);

  /// <summary>
  /// Links a leased target to the durable profile command or records its queue rejection.
  /// </summary>
  Task CompleteDispatchAsync(
      Guid campaignId,
      Guid targetId,
      string leaseOwner,
      ImageRolloutCommandQueueResult result,
      DateTimeOffset completedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Removes terminal campaigns beyond configured age and per-tenant bounds.
  /// </summary>
  Task PruneAsync(
      DateTimeOffset retainedAfter,
      int maximumCampaignsPerTenant,
      CancellationToken cancellationToken);
}
