using System.Security.Claims;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Fleet;

internal interface IRecoverManagerUnitOfWork
{
  Task<RecoveryCommandQueueResult?> QueueOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      RecoveryCommandFences fences,
      CancellationToken cancellationToken);
}

internal sealed class RecoverManagerUnitOfWork(
    IRecoveryCommandStore _commandStore,
    IAuthenticatedDashboardUserAccessor _userAccessor,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : IRecoverManagerUnitOfWork
{
  public Task<RecoveryCommandQueueResult?> QueueOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      RecoveryCommandFences fences,
      CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    if (user is null)
    {
      return Task.FromResult<RecoveryCommandQueueResult?>(null);
    }

    var options = _options.Value;
    var requestedAt = _timeProvider.GetUtcNow();
    return QueueAsync();

    async Task<RecoveryCommandQueueResult?> QueueAsync() =>
        await _commandStore.QueueAsync(
            tenantId,
            nodeId,
            profileId,
            fences,
            user.GitHubUserId,
            requestedAt,
            requestedAt.AddMinutes(options.RecoveryCommandLifetimeMinutes),
            requestedAt.AddSeconds(
                -options.RecoveryCapabilityFreshnessSeconds),
            requestedAt.AddSeconds(
                -options.RecoveryCommandCooldownSeconds),
            cancellationToken);
  }
}
