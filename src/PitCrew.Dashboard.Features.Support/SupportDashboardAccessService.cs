using System.Security.Claims;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Support;

internal sealed record SupportDashboardAccess(
    DashboardUser User,
    bool IsSystemAdministrator);

internal sealed class SupportDashboardAccessService(
    IAuthenticatedDashboardUserAccessor _userAccessor,
    IAccessStore _accessStore,
    IOptions<DashboardAuthenticationOptions> _options,
    TimeProvider _timeProvider)
{
  public async Task<SupportDashboardAccess?> GetOrNullAsync(
      ClaimsPrincipal principal,
      CancellationToken cancellationToken)
  {
    var authenticated = _userAccessor.GetOrNull(principal);
    if (authenticated is null)
    {
      return null;
    }
    var user = new DashboardUser(
        authenticated.GitHubUserId,
        authenticated.GitHubLogin,
        authenticated.DisplayName,
        authenticated.AvatarUrl);
    await _accessStore.UpsertUserAsync(
        user,
        _timeProvider.GetUtcNow(),
        cancellationToken);
    return new SupportDashboardAccess(
        user,
        _options.Value.Mode == DashboardAuthenticationMode.Development ||
        _options.Value.SystemAdministratorGitHubIds.Contains(
            user.GitHubUserId,
            StringComparer.Ordinal));
  }

  public async Task<bool> IsTenantAdministratorAsync(
      SupportDashboardAccess access,
      string tenantId,
      CancellationToken cancellationToken)
  {
    if (access.IsSystemAdministrator)
    {
      return true;
    }
    var role = await _accessStore.GetRoleOrNullAsync(
        tenantId,
        access.User.GitHubUserId,
        cancellationToken);
    return role is not null && role.Value >= TenantRole.Administrator;
  }
}
