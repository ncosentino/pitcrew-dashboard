using System.Security.Claims;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images;

internal interface IImageRecipeRegistrationUnitOfWork
{
  Task<RegisterImageRecipeCommandResult> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      RegisterImageRecipeInput input,
      CancellationToken cancellationToken);

  Task<ImageRecipeRegistrationPage> ListAsync(
      string tenantId,
      bool includeDisabled,
      int limit,
      CancellationToken cancellationToken);

  Task<ImageRecipeRegistration?> GetOrNullAsync(
      string tenantId,
      Guid registrationId,
      CancellationToken cancellationToken);

  Task<DisableImageRecipeRegistrationStatus> DisableAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid registrationId,
      CancellationToken cancellationToken);
}
