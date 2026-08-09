using System.Security.Claims;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class UnacknowledgeAlertUnitOfWork(
    IAlertIncidentStore _incidentStore,
    IAuthenticatedDashboardUserAccessor _userAccessor,
    TimeProvider _timeProvider) : IUnacknowledgeAlertUnitOfWork
{
  public Task<AlertUnacknowledgeStatus?> UnacknowledgeOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid incidentId,
      CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    return user is null
        ? Task.FromResult<AlertUnacknowledgeStatus?>(null)
        : ExecuteAsync();

    async Task<AlertUnacknowledgeStatus?> ExecuteAsync() =>
        await _incidentStore.UnacknowledgeAsync(
            tenantId,
            incidentId,
            _timeProvider.GetUtcNow(),
            cancellationToken);
  }
}
