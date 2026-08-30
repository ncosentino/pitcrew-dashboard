using System.Security.Claims;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;
using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class ImageRolloutCampaignOperations(
    IFleetStore _fleetStore,
    IImageRolloutCommandStore _commandStore,
    IImageRolloutCampaignStore _campaignStore,
    IAuthenticatedDashboardUserAccessor _userAccessor,
    IOptions<FleetDashboardOptions> _fleetOptions,
    IOptions<ImageRolloutCampaignOptions> _campaignOptions,
    TimeProvider _timeProvider) : IImageRolloutCampaignOperations
{
  public async Task<ImageRolloutCampaignMutation?>
      CreateForwardDraftOrNullAsync(
          ClaimsPrincipal principal,
          string tenantId,
          ImageRolloutCandidateAuthority candidate,
          string idempotencyKey,
          CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    if (user is null)
    {
      return null;
    }
    var now = _timeProvider.GetUtcNow();
    var targets = await CreateForwardTargetsAsync(
        tenantId,
        candidate,
        now,
        cancellationToken);
    var targetSetHash = ImageRolloutCampaignSignatures.ComputeTargetSetHash(
        ImageRolloutCampaignKind.Forward,
        null,
        candidate,
        targets);
    var plan = new ImageRolloutCampaignPlan(
        Guid.NewGuid(),
        tenantId,
        ImageRolloutCampaignKind.Forward,
        null,
        candidate,
        targetSetHash,
        user.GitHubUserId,
        now,
        targets,
        idempotencyKey,
        ImageRolloutCampaignSignatures.CreateForward(
            tenantId,
            user.GitHubUserId,
            candidate.CandidateId));
    return await _campaignStore.CreateAsync(
        plan,
        _campaignOptions.Value.MaximumTargets,
        cancellationToken);
  }

  public async Task<ImageRolloutCampaignMutation?> ConfigureOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignConfiguration configuration,
      string idempotencyKey,
      CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    if (user is null)
    {
      return null;
    }
    return await _campaignStore.ConfigureAsync(
        tenantId,
        campaignId,
        configuration,
        user.GitHubUserId,
        idempotencyKey,
        ImageRolloutCampaignSignatures.Configure(
            tenantId,
            user.GitHubUserId,
            campaignId,
            configuration),
        _timeProvider.GetUtcNow(),
        cancellationToken);
  }

  public async Task<ImageRolloutCampaignMutation?>
      ApproveWaveOrNullAsync(
          ClaimsPrincipal principal,
          string tenantId,
          Guid campaignId,
          ImageRolloutCampaignWaveApproval approval,
          string idempotencyKey,
          CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    if (user is null)
    {
      return null;
    }
    return await _campaignStore.ApproveWaveAsync(
        tenantId,
        campaignId,
        approval,
        user.GitHubUserId,
        idempotencyKey,
        ImageRolloutCampaignSignatures.ApproveWave(
            tenantId,
            user.GitHubUserId,
            campaignId,
            approval),
        _timeProvider.GetUtcNow(),
        cancellationToken);
  }

  public Task<ImageRolloutCampaignMutation?> PauseOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken) =>
      ChangeStateOrNullAsync(
          principal,
          tenantId,
          campaignId,
          fence,
          idempotencyKey,
          "pause",
          _campaignStore.PauseAsync,
          cancellationToken);

  public Task<ImageRolloutCampaignMutation?> ResumeOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken) =>
      ChangeStateOrNullAsync(
          principal,
          tenantId,
          campaignId,
          fence,
          idempotencyKey,
          "resume",
          _campaignStore.ResumeAsync,
          cancellationToken);

  public Task<ImageRolloutCampaignMutation?> CancelOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence,
      string idempotencyKey,
      CancellationToken cancellationToken) =>
      ChangeStateOrNullAsync(
          principal,
          tenantId,
          campaignId,
          fence,
          idempotencyKey,
          "cancel",
          _campaignStore.CancelAsync,
          cancellationToken);

  public async Task<ImageRolloutCampaignMutation?>
      CreateRollbackDraftOrNullAsync(
          ClaimsPrincipal principal,
          string tenantId,
          Guid sourceCampaignId,
          string idempotencyKey,
          CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    if (user is null)
    {
      return null;
    }
    var source = await _campaignStore.GetOrNullAsync(
        tenantId,
        sourceCampaignId,
        cancellationToken);
    if (source is null)
    {
      return new ImageRolloutCampaignMutation(
          ImageRolloutCampaignMutationOutcome.NotFound,
          null);
    }
    if (source.Status is not (
        ImageRolloutCampaignStatus.Complete or
        ImageRolloutCampaignStatus.Partial))
    {
      return new ImageRolloutCampaignMutation(
          ImageRolloutCampaignMutationOutcome.InvalidState,
          null);
    }
    var now = _timeProvider.GetUtcNow();
    var fleet = await LoadFleetAsync(tenantId, now, cancellationToken);
    var controls = await _commandStore.GetControlsAsync(
        tenantId,
        _fleetOptions.Value.ImageRolloutCapabilityFreshnessSeconds,
        cancellationToken,
        historyPerProfile: 1);
    var targets = ImageRolloutCampaignPlanner.CreateRollbackTargets(
        source,
        fleet,
        controls,
        now,
        _fleetOptions.Value.ImageRolloutCapabilityFreshnessSeconds);
    var targetSetHash = ImageRolloutCampaignSignatures.ComputeTargetSetHash(
        ImageRolloutCampaignKind.Rollback,
        sourceCampaignId,
        null,
        targets);
    var plan = new ImageRolloutCampaignPlan(
        Guid.NewGuid(),
        tenantId,
        ImageRolloutCampaignKind.Rollback,
        sourceCampaignId,
        null,
        targetSetHash,
        user.GitHubUserId,
        now,
        targets,
        idempotencyKey,
        ImageRolloutCampaignSignatures.CreateRollback(
            tenantId,
            user.GitHubUserId,
            sourceCampaignId));
    return await _campaignStore.CreateAsync(
        plan,
        _campaignOptions.Value.MaximumTargets,
        cancellationToken);
  }

  private async Task<IReadOnlyList<ImageRolloutCampaignPlannedTarget>>
      CreateForwardTargetsAsync(
          string tenantId,
          ImageRolloutCandidateAuthority candidate,
          DateTimeOffset now,
          CancellationToken cancellationToken)
  {
    var fleet = await LoadFleetAsync(tenantId, now, cancellationToken);
    var controls = await _commandStore.GetControlsAsync(
        tenantId,
        _fleetOptions.Value.ImageRolloutCapabilityFreshnessSeconds,
        cancellationToken,
        historyPerProfile: 1);
    return ImageRolloutCampaignPlanner.CreateForwardTargets(
        fleet,
        controls,
        candidate,
        now,
        _fleetOptions.Value.ImageRolloutCapabilityFreshnessSeconds);
  }

  private async Task<FleetResponse> LoadFleetAsync(
      string tenantId,
      DateTimeOffset now,
      CancellationToken cancellationToken) =>
      await _fleetStore.GetFleetAsync(
          tenantId,
          now,
          TimeSpan.FromSeconds(
              _fleetOptions.Value.NodeOfflineAfterSeconds),
          cancellationToken);

  private async Task<ImageRolloutCampaignMutation?>
      ChangeStateOrNullAsync(
          ClaimsPrincipal principal,
          string tenantId,
          Guid campaignId,
          ImageRolloutCampaignMutationFence fence,
          string idempotencyKey,
          string action,
          Func<
              string,
              Guid,
              ImageRolloutCampaignMutationFence,
              string,
              string,
              string,
              DateTimeOffset,
              CancellationToken,
              Task<ImageRolloutCampaignMutation>> mutation,
          CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    if (user is null)
    {
      return null;
    }
    return await mutation(
        tenantId,
        campaignId,
        fence,
        user.GitHubUserId,
        idempotencyKey,
        ImageRolloutCampaignSignatures.ChangeState(
            action,
            tenantId,
            user.GitHubUserId,
            campaignId,
            fence),
        _timeProvider.GetUtcNow(),
        cancellationToken);
  }
}
