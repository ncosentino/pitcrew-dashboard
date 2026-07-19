namespace PitCrew.Dashboard.Features.Access.Abstractions;

/// <summary>
/// Defines the authorization level granted by one tenant membership.
/// </summary>
public enum TenantRole
{
  /// <summary>
  /// Grants read-only access to tenant fleet data.
  /// </summary>
  Viewer = 0,

  /// <summary>
  /// Grants fleet administration, enrollment, and node credential management.
  /// </summary>
  Administrator = 1,

  /// <summary>
  /// Grants administrator capabilities plus tenant membership management.
  /// </summary>
  Owner = 2,
}

/// <summary>
/// Represents the GitHub identity retained by the dashboard.
/// </summary>
/// <param name="GitHubUserId">Immutable GitHub user identifier.</param>
/// <param name="GitHubLogin">Current GitHub login.</param>
/// <param name="DisplayName">Operator-facing display name.</param>
/// <param name="AvatarUrl">GitHub avatar URL when available.</param>
public sealed record DashboardUser(
    string GitHubUserId,
    string GitHubLogin,
    string DisplayName,
    string? AvatarUrl);

/// <summary>
/// Represents one tenant available to an authenticated dashboard user.
/// </summary>
/// <param name="TenantId">Stable tenant identifier used in API routes.</param>
/// <param name="DisplayName">Operator-facing tenant name.</param>
/// <param name="Role">Membership role granted to the user.</param>
public sealed record TenantAccess(
    string TenantId,
    string DisplayName,
    TenantRole Role);

/// <summary>
/// Represents the authenticated dashboard session and its available tenant contexts.
/// </summary>
/// <param name="User">Authenticated GitHub identity.</param>
/// <param name="IsSystemAdministrator">Whether configuration grants deployment-wide administration.</param>
/// <param name="Tenants">Tenant contexts visible to the user.</param>
public sealed record DashboardSession(
    DashboardUser User,
    bool IsSystemAdministrator,
    IReadOnlyList<TenantAccess> Tenants);

/// <summary>
/// Represents one persisted tenant membership.
/// </summary>
/// <param name="User">Member GitHub identity.</param>
/// <param name="Role">Role granted within the tenant.</param>
/// <param name="CreatedAt">Time the membership was created.</param>
public sealed record TenantMember(
    DashboardUser User,
    TenantRole Role,
    DateTimeOffset CreatedAt);

/// <summary>
/// Describes the outcome of a tenant or membership mutation.
/// </summary>
public enum AccessMutationStatus
{
  /// <summary>
  /// The mutation completed successfully.
  /// </summary>
  Succeeded,

  /// <summary>
  /// The requested tenant or user does not exist.
  /// </summary>
  NotFound,

  /// <summary>
  /// The requested tenant identifier already exists.
  /// </summary>
  Conflict,

  /// <summary>
  /// The mutation would remove the tenant's final owner.
  /// </summary>
  LastOwner,
}
