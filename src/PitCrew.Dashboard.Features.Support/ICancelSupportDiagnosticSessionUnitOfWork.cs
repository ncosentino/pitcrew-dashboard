using System.Security.Claims;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal interface ICancelSupportDiagnosticSessionUnitOfWork
{
  Task<SupportMutationStatus> CancelAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid sessionId,
      CancellationToken cancellationToken);
}
