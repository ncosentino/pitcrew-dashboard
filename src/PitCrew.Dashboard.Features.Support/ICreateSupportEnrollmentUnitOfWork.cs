using System.Security.Claims;

using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal interface ICreateSupportEnrollmentUnitOfWork
{
  Task<CreatedSupportEnrollmentAuthorization?> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      CreateSupportEnrollmentInput input,
      CancellationToken cancellationToken);
}
