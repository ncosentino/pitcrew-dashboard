using System.Security.Claims;

using PitCrew.Dashboard.Features.Images.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Images;

internal sealed class ImageRolloutCampaignOrchestrator(
    IImageCandidateStore _candidateStore,
    IImageRolloutCampaignStore _campaignStore,
    IImageRolloutCampaignOperations _operations)
    : IImageRolloutCampaignOrchestrator
{
  public async Task<IReadOnlyList<ImageRolloutCampaignSummary>> ListAsync(
      string tenantId,
      int limit,
      CancellationToken cancellationToken) =>
      await _campaignStore.ListAsync(tenantId, limit, cancellationToken);

  public async Task<ImageRolloutCampaignState?> GetOrNullAsync(
      string tenantId,
      Guid campaignId,
      CancellationToken cancellationToken) =>
      await _campaignStore.GetOrNullAsync(
          tenantId,
          campaignId,
          cancellationToken);

  public async Task<ImageRolloutCampaignCommandOutcome>
      CreateForwardDraftAsync(
          ClaimsPrincipal principal,
          string tenantId,
          Guid candidateId,
          string idempotencyKey,
          CancellationToken cancellationToken)
  {
    if (candidateId == Guid.Empty ||
        !RollOutProfileImageOrchestrator.IsValidIdempotencyKey(
            idempotencyKey))
    {
      return Invalid(
          "invalid_image_campaign_request",
          "candidateId and Idempotency-Key are required.");
    }
    var details = await _candidateStore.GetCandidateOrNullAsync(
        tenantId,
        candidateId,
        cancellationToken);
    if (details?.Candidate is not ReadyImageCandidate ready)
    {
      return NotFound(
          "image_campaign_candidate_not_found",
          "The requested ready candidate was not found.");
    }
    if (ready.OutputMode != ImageCandidateOutputMode.Registry ||
        ready.ImmutableReference is null ||
        !IsValidDigest(ready.Digest))
    {
      return Invalid(
          "image_campaign_candidate_not_ready",
          "The requested candidate is not a ready immutable registry image.");
    }
    var result = await _operations.CreateForwardDraftOrNullAsync(
        principal,
        tenantId,
        new ImageRolloutCandidateAuthority(
            ready.CandidateId,
            ready.RecipeId,
            ready.Digest,
            FormatPlatform(ready.Platform)),
        idempotencyKey,
        cancellationToken);
    return result is null
        ? Forbidden()
        : MapMutation(result, created: true);
  }

  public async Task<ImageRolloutCampaignCommandOutcome> ConfigureAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignConfiguration configuration,
      string idempotencyKey,
      CancellationToken cancellationToken)
  {
    if (campaignId == Guid.Empty ||
        configuration.WaveSize < 1 ||
        configuration.WaveSize >
            ImageRolloutCampaignConfiguration.MaximumWaveSize ||
        configuration.ExpectedRevision < 0 ||
        !IsValidHash(configuration.ExpectedTargetSetHash) ||
        !RollOutProfileImageOrchestrator.IsValidIdempotencyKey(
            idempotencyKey))
    {
      return Invalid(
          "invalid_image_campaign_configuration",
          "Campaign configuration is malformed.");
    }
    var result = await _operations.ConfigureOrNullAsync(
        principal,
        tenantId,
        campaignId,
        configuration,
        idempotencyKey,
        cancellationToken);
    return result is null ? Forbidden() : MapMutation(result);
  }

  public async Task<ImageRolloutCampaignCommandOutcome> ApproveWaveAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignWaveApproval approval,
      string idempotencyKey,
      CancellationToken cancellationToken)
  {
    if (campaignId == Guid.Empty ||
        approval.WaveNumber < 0 ||
        approval.ExpectedRevision < 0 ||
        !IsValidHash(approval.ExpectedTargetSetHash) ||
        !RollOutProfileImageOrchestrator.IsValidIdempotencyKey(
            idempotencyKey))
    {
      return Invalid(
          "invalid_image_campaign_wave_approval",
          "Campaign wave approval is malformed.");
    }
    var result = await _operations.ApproveWaveOrNullAsync(
        principal,
        tenantId,
        campaignId,
        approval,
        idempotencyKey,
        cancellationToken);
    return result is null ? Forbidden() : MapMutation(result);
  }

  public Task<ImageRolloutCampaignCommandOutcome> PauseAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken) =>
      ChangeStateAsync(
          principal,
          tenantId,
          campaignId,
          fence,
          idempotencyKey,
          _operations.PauseOrNullAsync,
          cancellationToken);

  public Task<ImageRolloutCampaignCommandOutcome> ResumeAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken) =>
      ChangeStateAsync(
          principal,
          tenantId,
          campaignId,
          fence,
          idempotencyKey,
          _operations.ResumeOrNullAsync,
          cancellationToken);

  public Task<ImageRolloutCampaignCommandOutcome> CancelAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken) =>
      ChangeStateAsync(
          principal,
          tenantId,
          campaignId,
          fence,
          idempotencyKey,
          _operations.CancelOrNullAsync,
          cancellationToken);

  public async Task<ImageRolloutCampaignCommandOutcome>
      CreateRollbackDraftAsync(
          ClaimsPrincipal principal,
          string tenantId,
          Guid sourceCampaignId,
          string idempotencyKey,
          CancellationToken cancellationToken)
  {
    if (sourceCampaignId == Guid.Empty ||
        !RollOutProfileImageOrchestrator.IsValidIdempotencyKey(
            idempotencyKey))
    {
      return Invalid(
          "invalid_image_campaign_rollback_request",
          "sourceCampaignId and Idempotency-Key are required.");
    }
    var result = await _operations.CreateRollbackDraftOrNullAsync(
        principal,
        tenantId,
        sourceCampaignId,
        idempotencyKey,
        cancellationToken);
    return result is null
        ? Forbidden()
        : MapMutation(result, created: true);
  }

  private static async Task<ImageRolloutCampaignCommandOutcome>
      ChangeStateAsync(
          ClaimsPrincipal principal,
          string tenantId,
          Guid campaignId,
          ImageRolloutCampaignMutationFence fence,
          string idempotencyKey,
          Func<
              ClaimsPrincipal,
              string,
              Guid,
              ImageRolloutCampaignMutationFence,
              string,
              CancellationToken,
              Task<ImageRolloutCampaignMutation?>> mutation,
          CancellationToken cancellationToken)
  {
    if (campaignId == Guid.Empty ||
        fence.ExpectedRevision < 0 ||
        !IsValidHash(fence.ExpectedTargetSetHash) ||
        !RollOutProfileImageOrchestrator.IsValidIdempotencyKey(
            idempotencyKey))
    {
      return Invalid(
          "invalid_image_campaign_mutation",
          "Campaign mutation fences are malformed.");
    }
    var result = await mutation(
        principal,
        tenantId,
        campaignId,
        fence,
        idempotencyKey,
        cancellationToken);
    return result is null ? Forbidden() : MapMutation(result);
  }

  private static ImageRolloutCampaignCommandOutcome MapMutation(
      ImageRolloutCampaignMutation result,
      bool created = false) =>
      result.Outcome switch
      {
        ImageRolloutCampaignMutationOutcome.Succeeded => new(
            created
                ? ImageRolloutCampaignCommandStatus.Created
                : ImageRolloutCampaignCommandStatus.Updated,
            result.Campaign,
            null,
            null),
        ImageRolloutCampaignMutationOutcome.IdempotentReplay => new(
            ImageRolloutCampaignCommandStatus.IdempotentReplay,
            result.Campaign,
            null,
            null),
        ImageRolloutCampaignMutationOutcome.IdempotencyKeyReuseConflict => new(
            ImageRolloutCampaignCommandStatus.Conflict,
            null,
            "image_campaign_idempotency_key_conflict",
            "The Idempotency-Key was already used with different campaign authority."),
        ImageRolloutCampaignMutationOutcome.NotFound => NotFound(
            "image_campaign_not_found",
            "The requested campaign was not found."),
        ImageRolloutCampaignMutationOutcome.InvalidState => new(
            ImageRolloutCampaignCommandStatus.Conflict,
            null,
            "image_campaign_invalid_state",
            "The campaign lifecycle does not allow this mutation."),
        ImageRolloutCampaignMutationOutcome.StaleFence => new(
            ImageRolloutCampaignCommandStatus.Conflict,
            null,
            "image_campaign_stale_fence",
            "The campaign revision or target-set hash no longer matches."),
        ImageRolloutCampaignMutationOutcome.InvalidCanary => Invalid(
            "image_campaign_invalid_canary",
            "The selected canary is not an eligible frozen target."),
        ImageRolloutCampaignMutationOutcome.TargetLimitExceeded => new(
            ImageRolloutCampaignCommandStatus.TargetLimitExceeded,
            null,
            "image_campaign_target_limit_exceeded",
            "The fleet exceeds the configured hard campaign target ceiling."),
        ImageRolloutCampaignMutationOutcome.RollbackAuthorityUnavailable => new(
            ImageRolloutCampaignCommandStatus.RollbackAuthorityUnavailable,
            null,
            "image_campaign_rollback_authority_unavailable",
            "No target has sufficient prior image authority for rollback."),
        _ => Invalid(
            "invalid_image_campaign_request",
            "Unsupported campaign mutation result."),
      };

  private static ImageRolloutCampaignCommandOutcome Forbidden() =>
      new(
          ImageRolloutCampaignCommandStatus.Forbidden,
          null,
          "forbidden_image_campaign",
          "The request principal is not an authenticated dashboard user.");

  private static ImageRolloutCampaignCommandOutcome Invalid(
      string code,
      string error) =>
      new(ImageRolloutCampaignCommandStatus.Invalid, null, code, error);

  private static ImageRolloutCampaignCommandOutcome NotFound(
      string code,
      string error) =>
      new(ImageRolloutCampaignCommandStatus.NotFound, null, code, error);

  private static bool IsValidDigest(string value) =>
      value is { Length: 71 } &&
      value.StartsWith("sha256:", StringComparison.Ordinal) &&
      IsAllLowerHex(value.AsSpan(7));

  private static bool IsValidHash(string value) =>
      value is { Length: 64 } &&
      IsAllLowerHex(value.AsSpan());

  private static bool IsAllLowerHex(ReadOnlySpan<char> value)
  {
    foreach (var character in value)
    {
      if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
      {
        return false;
      }
    }
    return true;
  }

  private static string FormatPlatform(ImageCandidatePlatform platform) =>
      platform switch
      {
        ImageCandidatePlatform.LinuxAmd64 => "linux/amd64",
        ImageCandidatePlatform.LinuxArm64 => "linux/arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
      };
}
