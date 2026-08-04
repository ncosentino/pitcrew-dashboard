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

/// <summary>
/// Describes the outcome of a diagnostic-credential mutation.
/// </summary>
public enum DiagnosticCredentialMutationStatus
{
  /// <summary>
  /// The credential mutation completed.
  /// </summary>
  Succeeded,

  /// <summary>
  /// The credential or tenant does not exist.
  /// </summary>
  NotFound,

  /// <summary>
  /// A requested node is not owned by the tenant.
  /// </summary>
  InvalidNode,

  /// <summary>
  /// The credential state conflicts with the requested mutation.
  /// </summary>
  Conflict,
}

/// <summary>
/// Defines one scoped, read-only diagnostic credential.
/// </summary>
/// <param name="CredentialId">Dashboard-assigned credential identifier.</param>
/// <param name="TenantId">Only tenant the credential may read.</param>
/// <param name="Label">Operator-facing credential purpose.</param>
/// <param name="CreatedByGitHubUserId">Administrator that created the credential.</param>
/// <param name="CreatedAt">Time the credential was created.</param>
/// <param name="ExpiresAt">Time after which authentication fails.</param>
/// <param name="RevokedAt">Revocation time when the credential is inactive.</param>
/// <param name="RevokedByGitHubUserId">Administrator that revoked the credential.</param>
/// <param name="RotatedFromCredentialId">Credential replaced by this credential.</param>
/// <param name="LastUsedAt">Most recent successful authentication time.</param>
/// <param name="UseCount">Successful authentication count.</param>
/// <param name="NodeIds">Allowed nodes, or empty for every tenant node.</param>
/// <param name="ProfileIds">Allowed profiles, or empty for every profile.</param>
public sealed record DiagnosticCredential(
    Guid CredentialId,
    string TenantId,
    string Label,
    string CreatedByGitHubUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedByGitHubUserId,
    Guid? RotatedFromCredentialId,
    DateTimeOffset? LastUsedAt,
    long UseCount,
    IReadOnlyList<Guid> NodeIds,
    IReadOnlyList<string> ProfileIds);

/// <summary>
/// Carries a diagnostic credential definition into durable storage.
/// </summary>
/// <param name="Credential">Non-secret credential metadata.</param>
/// <param name="TokenHash">One-way SHA-256 hash of the raw credential.</param>
public sealed record DiagnosticCredentialWrite(
    DiagnosticCredential Credential,
    string TokenHash);

/// <summary>
/// Represents one authenticated diagnostic scope.
/// </summary>
/// <param name="CredentialId">Credential that authenticated the request.</param>
/// <param name="TenantId">Only tenant the credential may read.</param>
/// <param name="NodeIds">Allowed nodes, or empty for every tenant node.</param>
/// <param name="ProfileIds">Allowed profiles, or empty for every profile.</param>
public sealed record DiagnosticAccessScope(
    Guid CredentialId,
    string TenantId,
    IReadOnlyList<Guid> NodeIds,
    IReadOnlyList<string> ProfileIds);

/// <summary>
/// Returns a diagnostic-credential mutation and any resulting credential metadata.
/// </summary>
/// <param name="Status">Mutation outcome.</param>
/// <param name="Credential">Created credential metadata when successful.</param>
public sealed record DiagnosticCredentialMutation(
    DiagnosticCredentialMutationStatus Status,
    DiagnosticCredential? Credential);
