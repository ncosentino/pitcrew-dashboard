using System.Security.Claims;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Fleet;

internal interface ICreateEnrollmentCodeUnitOfWork
{
  Task<CreatedEnrollmentCode?> CreateOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      string label,
      CancellationToken cancellationToken);
}

internal sealed class CreateEnrollmentCodeUnitOfWork(
    IFleetStore _fleetStore,
    ConnectorCredentialService _credentialService,
    IAuthenticatedDashboardUserAccessor _userAccessor,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : ICreateEnrollmentCodeUnitOfWork
{
  public async Task<CreatedEnrollmentCode?> CreateOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      string label,
      CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    if (user is null)
    {
      return null;
    }

    var createdAt = _timeProvider.GetUtcNow();
    var expiresAt = createdAt.AddMinutes(
        _options.Value.EnrollmentCodeLifetimeMinutes);
    var code = _credentialService.CreateEnrollmentCode();
    var codeId = Guid.NewGuid();
    await _fleetStore.CreateEnrollmentCodeAsync(
        codeId,
        tenantId,
        _credentialService.Hash(code),
        label,
        user.GitHubUserId,
        createdAt,
        expiresAt,
        cancellationToken);
    return new CreatedEnrollmentCode(
        codeId,
        code,
        expiresAt);
  }
}
