using System.Security.Claims;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Access;

internal sealed record AccessContext(
    DashboardUser User,
    bool IsSystemAdministrator);

internal sealed class AccessContextService(
    IAuthenticatedDashboardUserAccessor _userAccessor,
    IAccessStore _accessStore,
    IOptions<DashboardAuthenticationOptions> _options,
    TimeProvider _timeProvider)
{
  public async Task<AccessContext?> GetOrNullAsync(
      ClaimsPrincipal principal,
      CancellationToken cancellationToken)
  {
    var authenticatedUser = _userAccessor.GetOrNull(principal);
    if (authenticatedUser is null)
    {
      return null;
    }

    var user = new DashboardUser(
        authenticatedUser.GitHubUserId,
        authenticatedUser.GitHubLogin,
        authenticatedUser.DisplayName,
        authenticatedUser.AvatarUrl);
    await _accessStore.UpsertUserAsync(
        user,
        _timeProvider.GetUtcNow(),
        cancellationToken);
    return new AccessContext(
        user,
        _options.Value.Mode == DashboardAuthenticationMode.Development ||
        _options.Value.SystemAdministratorGitHubIds.Contains(
            user.GitHubUserId,
            StringComparer.Ordinal));
  }
}
