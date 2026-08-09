using System.Security.Claims;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal interface IUnacknowledgeAlertUnitOfWork
{
  Task<AlertUnacknowledgeStatus?> UnacknowledgeOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid incidentId,
      CancellationToken cancellationToken);
}
