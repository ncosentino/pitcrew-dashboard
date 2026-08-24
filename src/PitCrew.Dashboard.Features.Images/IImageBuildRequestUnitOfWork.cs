using System.Security.Claims;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images;

internal interface IImageBuildRequestUnitOfWork
{
  Task<ImageBuildRequestCommandResult> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      RequestImageBuildInput input,
      CancellationToken cancellationToken);

  Task<IReadOnlyList<ImageBuildRequest>> ListAsync(
      string tenantId,
      int limit,
      CancellationToken cancellationToken);

  Task<ImageBuildRequest?> GetOrNullAsync(
      string tenantId,
      Guid requestId,
      CancellationToken cancellationToken);
}
