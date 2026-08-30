using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Images;

internal static class ImageRolloutCampaignResponseMapper
{
  public static ImageRolloutCampaignSummaryResponse ToResponse(
      ImageRolloutCampaignSummary summary) =>
      new(
          summary.CampaignId,
          FormatKind(summary.Kind),
          summary.SourceCampaignId,
          ToResponse(summary.Candidate),
          summary.TargetSetHash,
          FormatStatus(summary.Status),
          summary.Revision,
          summary.WaveSize,
          summary.EligibleTargetCount,
          summary.ExcludedTargetCount,
          summary.CompleteTargetCount,
          summary.AdverseTargetCount,
          summary.CurrentWaveNumber,
          summary.NextWaveNumber,
          summary.RequestedByGitHubUserId,
          summary.RequestedAt,
          summary.ConfiguredAt,
          summary.CompletedAt);

  public static ImageRolloutCampaignResponse ToResponse(
      ImageRolloutCampaignState campaign) =>
      new(
          campaign.CampaignId,
          FormatKind(campaign.Kind),
          campaign.SourceCampaignId,
          ToResponse(campaign.Candidate),
          campaign.TargetSetHash,
          FormatStatus(campaign.Status),
          campaign.Revision,
          campaign.WaveSize,
          campaign.RequestedByGitHubUserId,
          campaign.RequestedAt,
          campaign.ConfiguredByGitHubUserId,
          campaign.ConfiguredAt,
          campaign.PausedAt,
          campaign.CancelledAt,
          campaign.CompletedAt,
          campaign.Targets.Select(ToResponse).ToArray(),
          campaign.Waves.Select(ToResponse).ToArray());

  private static ImageRolloutCampaignTargetResponse ToResponse(
      ImageRolloutCampaignTargetState target) =>
      new(
          target.TargetId,
          target.NodeId,
          target.NodeDisplayName,
          target.ProfileId,
          ToResponse(target.Candidate),
          target.Fences?.ExpectedCurrentImageReference,
          target.Fences?.ExpectedCurrentImageDigest,
          target.Fences?.ExpectedCurrentLocalImageId,
          target.Fences?.ExpectedCurrentWorkerRevision,
          target.Fences?.ExpectedStaticFingerprint,
          target.Fences?.ExpectedPreservedConfigurationFingerprint,
          target.Fences?.ExpectedRoutingFingerprint,
          target.Fences?.ExpectedDesiredGeneration,
          target.Fences?.ExpectedDesiredStateHash,
          target.ExclusionCategory,
          FormatTargetStatus(target.Status),
          target.WaveNumber,
          target.IsCanary,
          target.CommandId,
          target.FailureCategory,
          target.ResultMessage,
          target.TargetWorkerRevision,
          target.ManagerConvergenceStatus,
          target.CurrentWorkers,
          target.StaleWorkers,
          target.ClaimedAt,
          target.StartedAt,
          target.CompletedAt,
          target.PreviousCandidateId,
          target.PreviousRecipeId,
          target.PreviousImageReference,
          target.PreviousImageDigest,
          target.PreviousWorkerRevision);

  private static ImageRolloutCampaignWaveResponse ToResponse(
      ImageRolloutCampaignWaveState wave) =>
      new(
          wave.WaveNumber,
          FormatWaveStatus(wave.Status),
          wave.TargetCount,
          wave.ApprovedByGitHubUserId,
          wave.ApprovedAt,
          wave.CompletedAt);

  private static ImageRolloutCampaignCandidateResponse? ToResponse(
      ImageRolloutCandidateAuthority? candidate) =>
      candidate is null
          ? null
          : new ImageRolloutCampaignCandidateResponse(
              candidate.CandidateId,
              candidate.RecipeId,
              candidate.TargetDigest,
              candidate.TargetPlatform);

  private static string FormatKind(ImageRolloutCampaignKind kind) =>
      kind switch
      {
        ImageRolloutCampaignKind.Forward => "forward",
        ImageRolloutCampaignKind.Rollback => "rollback",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
      };

  private static string FormatStatus(ImageRolloutCampaignStatus status) =>
      status switch
      {
        ImageRolloutCampaignStatus.Draft => "draft",
        ImageRolloutCampaignStatus.AwaitingApproval => "awaiting-approval",
        ImageRolloutCampaignStatus.Running => "running",
        ImageRolloutCampaignStatus.Paused => "paused",
        ImageRolloutCampaignStatus.Complete => "complete",
        ImageRolloutCampaignStatus.Partial => "partial",
        ImageRolloutCampaignStatus.Blocked => "blocked",
        ImageRolloutCampaignStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
      };

  private static string FormatTargetStatus(
      ImageRolloutCampaignTargetStatus status) =>
      status switch
      {
        ImageRolloutCampaignTargetStatus.Eligible => "eligible",
        ImageRolloutCampaignTargetStatus.Excluded => "excluded",
        ImageRolloutCampaignTargetStatus.Queued => "queued",
        ImageRolloutCampaignTargetStatus.Claimed => "claimed",
        ImageRolloutCampaignTargetStatus.Applying => "applying",
        ImageRolloutCampaignTargetStatus.Rolling => "rolling",
        ImageRolloutCampaignTargetStatus.Complete => "complete",
        ImageRolloutCampaignTargetStatus.Failed => "failed",
        ImageRolloutCampaignTargetStatus.Blocked => "blocked",
        ImageRolloutCampaignTargetStatus.Indeterminate => "indeterminate",
        ImageRolloutCampaignTargetStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
      };

  private static string FormatWaveStatus(
      ImageRolloutCampaignWaveStatus status) =>
      status switch
      {
        ImageRolloutCampaignWaveStatus.Pending => "pending",
        ImageRolloutCampaignWaveStatus.Approved => "approved",
        ImageRolloutCampaignWaveStatus.Running => "running",
        ImageRolloutCampaignWaveStatus.Complete => "complete",
        ImageRolloutCampaignWaveStatus.Blocked => "blocked",
        ImageRolloutCampaignWaveStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
      };
}
