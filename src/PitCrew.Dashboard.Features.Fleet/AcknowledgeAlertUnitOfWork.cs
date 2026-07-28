using System.Security.Claims;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class AcknowledgeAlertUnitOfWork(
    IAlertIncidentStore _incidentStore,
    IAuthenticatedDashboardUserAccessor _userAccessor,
    TimeProvider _timeProvider) : IAcknowledgeAlertUnitOfWork
{
  public Task<AlertAcknowledgeStatus?> AcknowledgeOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid incidentId,
      CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    return user is null
        ? Task.FromResult<AlertAcknowledgeStatus?>(null)
        : AcknowledgeAsync(user.GitHubUserId);

    async Task<AlertAcknowledgeStatus?> AcknowledgeAsync(
        string githubUserId) =>
        await _incidentStore.AcknowledgeAsync(
            tenantId,
            incidentId,
            githubUserId,
            _timeProvider.GetUtcNow(),
            cancellationToken);
  }
}
