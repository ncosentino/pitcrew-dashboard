using System.Security.Claims;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal interface ICreateSupportDiagnosticSessionUnitOfWork
{
  Task<SupportSessionMutation> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      SupportDiagnosticSessionInput input,
      CancellationToken cancellationToken);
}
