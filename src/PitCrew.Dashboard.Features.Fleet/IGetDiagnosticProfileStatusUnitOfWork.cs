using System.Security.Claims;

namespace PitCrew.Dashboard.Features.Fleet;

internal interface IGetDiagnosticProfileStatusUnitOfWork
{
  Task<DiagnosticProfileStatusQueryResult> GetAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      CancellationToken cancellationToken);
}
