using System.Security.Claims;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal interface IRevokeSupportIdentityUnitOfWork
{
  Task<SupportMutationStatus> RevokeAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      CancellationToken cancellationToken);
}
