using System.Security.Claims;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal interface IGetSupportDiagnosticSessionUnitOfWork
{
  Task<SupportSessionMutation> GetAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid sessionId,
      CancellationToken cancellationToken);

  Task<IReadOnlyList<SupportDiagnosticSession>> GetRecentAsync(
      ClaimsPrincipal principal,
      string tenantId,
      CancellationToken cancellationToken);
}
