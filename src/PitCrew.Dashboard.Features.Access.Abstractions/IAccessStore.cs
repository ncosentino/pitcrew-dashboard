namespace PitCrew.Dashboard.Features.Access.Abstractions;

/// <summary>
/// Persists dashboard users, tenants, and tenant memberships.
/// </summary>
public interface IAccessStore
{
  /// <summary>
  /// Creates or refreshes the stored profile for one authenticated GitHub user.
  /// </summary>
  /// <param name="user">Authenticated GitHub identity.</param>
  /// <param name="observedAt">Dashboard time when the identity was observed.</param>
  /// <param name="cancellationToken">Token that cancels the operation.</param>
  /// <returns>A task that completes after the user profile is persisted.</returns>
  Task UpsertUserAsync(
      DashboardUser user,
      DateTimeOffset observedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads the session tenant contexts visible to one GitHub user.
  /// </summary>
  /// <param name="user">Authenticated GitHub identity.</param>
  /// <param name="isSystemAdministrator">Whether all tenant contexts should be visible.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The current dashboard session.</returns>
  Task<DashboardSession> GetSessionAsync(
      DashboardUser user,
      bool isSystemAdministrator,
      CancellationToken cancellationToken);

  /// <summary>
  /// Reads the user's role within one tenant.
  /// </summary>
  /// <param name="tenantId">Tenant route identifier.</param>
  /// <param name="githubUserId">Immutable GitHub user identifier.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The membership role, or <see langword="null"/> when no membership exists.</returns>
  Task<TenantRole?> GetRoleOrNullAsync(
      string tenantId,
      string githubUserId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Creates a tenant and grants ownership to its creator.
  /// </summary>
  /// <param name="tenantId">Stable tenant route identifier.</param>
  /// <param name="displayName">Operator-facing tenant name.</param>
  /// <param name="ownerGitHubUserId">GitHub user that receives the initial owner membership.</param>
  /// <param name="createdAt">Dashboard time of creation.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>The mutation status.</returns>
  Task<AccessMutationStatus> CreateTenantAsync(
      string tenantId,
      string displayName,
      string ownerGitHubUserId,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Ensures one development tenant and owner membership exist.
  /// </summary>
  /// <param name="tenantId">Stable tenant route identifier.</param>
  /// <param name="displayName">Operator-facing tenant name.</param>
  /// <param name="owner">Development operator identity.</param>
  /// <param name="createdAt">Dashboard time used for missing records.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>A task that completes after the bootstrap records exist.</returns>
  Task EnsureTenantOwnerAsync(
      string tenantId,
      string displayName,
      DashboardUser owner,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Lists memberships for one tenant.
  /// </summary>
  /// <param name="tenantId">Tenant route identifier.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Tenant memberships ordered by GitHub login.</returns>
  Task<IReadOnlyList<TenantMember>> GetMembersAsync(
      string tenantId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Lists authenticated users that do not currently belong to one tenant.
  /// </summary>
  /// <param name="tenantId">Tenant route identifier.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Known users available for membership assignment.</returns>
  Task<IReadOnlyList<DashboardUser>> GetAvailableUsersAsync(
      string tenantId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Creates or changes one tenant membership.
  /// </summary>
  /// <param name="tenantId">Tenant route identifier.</param>
  /// <param name="githubUserId">Target GitHub user identifier.</param>
  /// <param name="role">Role to grant.</param>
  /// <param name="createdByGitHubUserId">GitHub user performing the mutation.</param>
  /// <param name="createdAt">Dashboard time used for a new membership.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>The mutation status.</returns>
  Task<AccessMutationStatus> SetMembershipAsync(
      string tenantId,
      string githubUserId,
      TenantRole role,
      string createdByGitHubUserId,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Removes one tenant membership.
  /// </summary>
  /// <param name="tenantId">Tenant route identifier.</param>
  /// <param name="githubUserId">Target GitHub user identifier.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>The mutation status.</returns>
  Task<AccessMutationStatus> RemoveMembershipAsync(
      string tenantId,
      string githubUserId,
      CancellationToken cancellationToken);
}
