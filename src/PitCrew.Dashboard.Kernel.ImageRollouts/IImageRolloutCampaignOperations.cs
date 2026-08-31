using System.Security.Claims;

namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Authorizes and composes fleet evidence for image rollout campaign mutations.
/// </summary>
public interface IImageRolloutCampaignOperations
{
  /// <summary>
  /// Creates one frozen forward campaign draft for a ready candidate.
  /// </summary>
  Task<ImageRolloutCampaignMutation?> CreateForwardDraftOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      ImageRolloutCandidateAuthority candidate,
      string idempotencyKey,
      CancellationToken cancellationToken);

  /// <summary>
  /// Freezes canary and wave configuration for a draft.
  /// </summary>
  Task<ImageRolloutCampaignMutation?> ConfigureOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignConfiguration configuration,
      string idempotencyKey,
      CancellationToken cancellationToken);

  /// <summary>
  /// Approves one pending campaign wave.
  /// </summary>
  Task<ImageRolloutCampaignMutation?> ApproveWaveOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignWaveApproval approval,
      string idempotencyKey,
      CancellationToken cancellationToken);

  /// <summary>
  /// Pauses future dispatch for one campaign.
  /// </summary>
  Task<ImageRolloutCampaignMutation?> PauseOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken);

  /// <summary>
  /// Resumes future dispatch for one paused campaign.
  /// </summary>
  Task<ImageRolloutCampaignMutation?> ResumeOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken);

  /// <summary>
  /// Cancels undispatched targets for one campaign.
  /// </summary>
  Task<ImageRolloutCampaignMutation?> CancelOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken);

  /// <summary>
  /// Creates one frozen rollback campaign from proven prior target authority.
  /// </summary>
  Task<ImageRolloutCampaignMutation?> CreateRollbackDraftOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid sourceCampaignId,
      string idempotencyKey,
      CancellationToken cancellationToken);
}
