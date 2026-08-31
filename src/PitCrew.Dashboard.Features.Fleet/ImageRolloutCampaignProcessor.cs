using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class ImageRolloutCampaignProcessor(
    IImageRolloutCampaignStore _campaignStore,
    IImageRolloutCommandStore _commandStore,
    IOptions<FleetDashboardOptions> _fleetOptions,
    IOptions<ImageRolloutCampaignOptions> _campaignOptions,
    TimeProvider _timeProvider) : IImageRolloutCampaignProcessor
{
  private readonly string _leaseOwner = $"image-campaigns-{Guid.NewGuid():N}";
  private DateTimeOffset _nextPruneAt = DateTimeOffset.MinValue;

  public async Task<int> ProcessOnceAsync(
      CancellationToken cancellationToken)
  {
    var now = _timeProvider.GetUtcNow();
    var options = _campaignOptions.Value;
    await _campaignStore.ReconcileAsync(
        now,
        _fleetOptions.Value.ImageRolloutCapabilityFreshnessSeconds,
        options.MaximumCampaignsPerReconciliation,
        cancellationToken);
    if (now >= _nextPruneAt)
    {
      await _campaignStore.PruneAsync(
          now.AddDays(-options.RetentionDays),
          options.MaximumCampaignsPerTenant,
          cancellationToken);
      _nextPruneAt = now.AddHours(1);
    }
    var claims = await _campaignStore.ClaimDueTargetsAsync(
        _leaseOwner,
        now,
        now.AddSeconds(options.ClaimLeaseSeconds),
        maximumClaims: 1,
        options.MaximumConcurrentTargetsPerCampaign,
        options.MaximumConcurrentTargetsPerNode,
        cancellationToken);
    if (claims.Count == 0)
    {
      return 0;
    }

    var claim = claims[0];
    var fleetOptions = _fleetOptions.Value;
    var signature = RollOutProfileImageUnitOfWork.ComputeSignature(
        claim.TenantId,
        claim.ApprovedByGitHubUserId,
        claim.NodeId,
        claim.ProfileId,
        claim.Candidate.CandidateId,
        claim.Fences);
    var result = await _commandStore.QueueAsync(
        claim.TenantId,
        claim.NodeId,
        claim.ProfileId,
        claim.Candidate,
        claim.Fences,
        claim.ApprovedByGitHubUserId,
        claim.IdempotencyKey,
        signature,
        now,
        now.AddMinutes(fleetOptions.ImageRolloutCommandLifetimeMinutes),
        now.AddSeconds(-fleetOptions.ImageRolloutCapabilityFreshnessSeconds),
        now.AddSeconds(-fleetOptions.ImageRolloutCommandCooldownSeconds),
        cancellationToken);
    await _campaignStore.CompleteDispatchAsync(
        claim.CampaignId,
        claim.TargetId,
        _leaseOwner,
        result,
        now,
        cancellationToken);
    return 1;
  }
}
