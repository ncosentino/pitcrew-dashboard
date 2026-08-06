using System.Security.Claims;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Fleet;

internal interface ISetCapacityMaximumUnitOfWork
{
  Task<CapacityCommandQueueResult?> QueueOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      int maximum,
      Guid? resumeCommandId,
      CancellationToken cancellationToken);
}

internal sealed class SetCapacityMaximumUnitOfWork(
    ICapacityCommandStore _commandStore,
    IAuthenticatedDashboardUserAccessor _userAccessor,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : ISetCapacityMaximumUnitOfWork
{
  public Task<CapacityCommandQueueResult?> QueueOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      int maximum,
      Guid? resumeCommandId,
      CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    if (user is null)
    {
      return Task.FromResult<CapacityCommandQueueResult?>(null);
    }

    var requestedAt = _timeProvider.GetUtcNow();
    return QueueAsync();

    async Task<CapacityCommandQueueResult?> QueueAsync() =>
        await _commandStore.QueueAsync(
            tenantId,
            nodeId,
            profileId,
            maximum,
            user.GitHubUserId,
            requestedAt,
            requestedAt.AddMinutes(
                _options.Value.CapacityCommandLifetimeMinutes),
            cancellationToken,
            resumeCommandId);
  }
}
