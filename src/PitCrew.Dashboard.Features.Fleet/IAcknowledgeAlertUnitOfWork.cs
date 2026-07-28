using System.Security.Claims;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal interface IAcknowledgeAlertUnitOfWork
{
  Task<AlertAcknowledgeStatus?> AcknowledgeOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid incidentId,
      CancellationToken cancellationToken);
}
