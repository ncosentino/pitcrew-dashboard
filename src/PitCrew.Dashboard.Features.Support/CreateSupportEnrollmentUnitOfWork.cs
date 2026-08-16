using System.Security.Claims;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Dashboard.Kernel.DisplayNames;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class CreateSupportEnrollmentUnitOfWork(
    SupportDashboardAccessService _accessContextService,
    ISupportStore _supportStore,
    SupportSecretService _secretService,
    IOptions<SupportPlaneOptions> _options,
    TimeProvider _timeProvider) : ICreateSupportEnrollmentUnitOfWork
{
  public async Task<CreatedSupportEnrollmentAuthorization?> CreateAsync(
      ClaimsPrincipal principal,
      string tenantId,
      CreateSupportEnrollmentInput input,
      CancellationToken cancellationToken)
  {
    var actor = await _accessContextService.GetOrNullAsync(principal, cancellationToken);
    if (actor is null ||
        !await _accessContextService.IsTenantAdministratorAsync(
            actor,
            tenantId,
            cancellationToken))
    {
      return null;
    }
    var displayName = OperatorDisplayName.NormalizeOrNull(input.DisplayName);
    if (displayName is null)
    {
      return null;
    }
    var now = _timeProvider.GetUtcNow();
    var enrollmentCode = _secretService.CreateEnrollmentCode();
    var expiresAt = now.AddSeconds(_options.Value.EnrollmentLifetimeSeconds);
    var enrollment = new SupportEnrollment(
        Guid.NewGuid(),
        tenantId,
        displayName,
        _secretService.Hash(enrollmentCode),
        actor.User.GitHubUserId,
        now,
        expiresAt,
        null,
        null,
        null,
        null,
        null);
    var status = await _supportStore.CreateEnrollmentAsync(
        enrollment,
        cancellationToken);
    return status == SupportMutationStatus.Succeeded
        ? new CreatedSupportEnrollmentAuthorization(
            displayName,
            enrollmentCode,
            expiresAt)
        : null;
  }
}
