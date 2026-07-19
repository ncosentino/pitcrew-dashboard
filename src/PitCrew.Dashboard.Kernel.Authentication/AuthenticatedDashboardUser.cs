using System.Security.Claims;

namespace PitCrew.Dashboard.Kernel.Authentication;

/// <summary>
/// Represents the authenticated GitHub identity used by dashboard authorization.
/// </summary>
/// <param name="GitHubUserId">Immutable GitHub user identifier.</param>
/// <param name="GitHubLogin">Current GitHub login.</param>
/// <param name="DisplayName">Operator-facing display name.</param>
/// <param name="AvatarUrl">GitHub avatar URL when available.</param>
public sealed record AuthenticatedDashboardUser(
    string GitHubUserId,
    string GitHubLogin,
    string DisplayName,
    string? AvatarUrl);

/// <summary>
/// Reads a validated dashboard identity from an authenticated claims principal.
/// </summary>
public interface IAuthenticatedDashboardUserAccessor
{
  /// <summary>
  /// Reads the dashboard identity when all required claims are present.
  /// </summary>
  /// <param name="principal">Claims principal created by dashboard authentication.</param>
  /// <returns>The authenticated user, or <see langword="null"/> when required claims are absent.</returns>
  AuthenticatedDashboardUser? GetOrNull(ClaimsPrincipal principal);
}

internal sealed class AuthenticatedDashboardUserAccessor :
    IAuthenticatedDashboardUserAccessor
{
  public AuthenticatedDashboardUser? GetOrNull(ClaimsPrincipal principal)
  {
    var githubUserId = principal.FindFirstValue(
        PitCrewClaimTypes.GitHubUserId);
    var githubLogin = principal.FindFirstValue(
        PitCrewClaimTypes.GitHubLogin);
    if (string.IsNullOrWhiteSpace(githubUserId) ||
        string.IsNullOrWhiteSpace(githubLogin))
    {
      return null;
    }

    var displayName = principal.FindFirstValue(ClaimTypes.Name);
    return new AuthenticatedDashboardUser(
        githubUserId,
        githubLogin,
        string.IsNullOrWhiteSpace(displayName)
            ? githubLogin
            : displayName,
        principal.FindFirstValue(PitCrewClaimTypes.AvatarUrl));
  }
}
