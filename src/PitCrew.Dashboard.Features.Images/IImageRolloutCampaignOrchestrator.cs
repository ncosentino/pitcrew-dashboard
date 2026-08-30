using System.Security.Claims;

using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Images;

internal interface IImageRolloutCampaignOrchestrator
{
  Task<IReadOnlyList<ImageRolloutCampaignSummary>> ListAsync(
      string tenantId,
      int limit,
      CancellationToken cancellationToken);

  Task<ImageRolloutCampaignState?> GetOrNullAsync(
      string tenantId,
      Guid campaignId,
      CancellationToken cancellationToken);

  Task<ImageRolloutCampaignCommandOutcome> CreateForwardDraftAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid candidateId,
      string idempotencyKey,
      CancellationToken cancellationToken);

  Task<ImageRolloutCampaignCommandOutcome> ConfigureAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignConfiguration configuration,
      string idempotencyKey,
      CancellationToken cancellationToken);

  Task<ImageRolloutCampaignCommandOutcome> ApproveWaveAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignWaveApproval approval,
      string idempotencyKey,
      CancellationToken cancellationToken);

  Task<ImageRolloutCampaignCommandOutcome> PauseAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken);

  Task<ImageRolloutCampaignCommandOutcome> ResumeAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken);

  Task<ImageRolloutCampaignCommandOutcome> CancelAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken);

  Task<ImageRolloutCampaignCommandOutcome> CreateRollbackDraftAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid sourceCampaignId,
      string idempotencyKey,
      CancellationToken cancellationToken);
}
