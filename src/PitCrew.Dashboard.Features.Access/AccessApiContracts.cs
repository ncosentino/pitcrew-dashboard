using System.Text.Json.Serialization;

namespace PitCrew.Dashboard.Features.Access;

/// <summary>
/// Returns one authenticated dashboard user.
/// </summary>
/// <param name="GitHubUserId">Immutable GitHub user identifier.</param>
/// <param name="GitHubLogin">Current GitHub login.</param>
/// <param name="DisplayName">Operator-facing display name.</param>
/// <param name="AvatarUrl">GitHub avatar URL when available.</param>
public sealed record DashboardUserResponse(
    [property: JsonPropertyName("githubUserId")] string GitHubUserId,
    [property: JsonPropertyName("githubLogin")] string GitHubLogin,
    string DisplayName,
    string? AvatarUrl);

/// <summary>
/// Returns one tenant context available to the current user.
/// </summary>
/// <param name="TenantId">Stable tenant route identifier.</param>
/// <param name="DisplayName">Operator-facing tenant name.</param>
/// <param name="Role">Role granted within the tenant.</param>
public sealed record TenantAccessResponse(
    string TenantId,
    string DisplayName,
    string Role);

/// <summary>
/// Returns the authenticated session and antiforgery request token.
/// </summary>
/// <param name="User">Authenticated dashboard user.</param>
/// <param name="IsSystemAdministrator">Whether deployment-wide administration is granted.</param>
/// <param name="Tenants">Available tenant contexts.</param>
/// <param name="AntiforgeryToken">Request token required by authenticated mutation endpoints.</param>
public sealed record DashboardSessionResponse(
    DashboardUserResponse User,
    bool IsSystemAdministrator,
    IReadOnlyList<TenantAccessResponse> Tenants,
    string AntiforgeryToken);

/// <summary>
/// Requests creation of one tenant.
/// </summary>
/// <param name="TenantId">Stable lowercase tenant route identifier.</param>
/// <param name="DisplayName">Operator-facing tenant name.</param>
public sealed record CreateTenantRequest(
    string TenantId,
    string DisplayName);

/// <summary>
/// Requests a new operator-facing name for one tenant.
/// </summary>
/// <param name="DisplayName">New operator-facing tenant name.</param>
public sealed record RenameTenantRequest(string DisplayName);

/// <summary>
/// Requests a role assignment for one tenant member.
/// </summary>
/// <param name="Role">Role name: viewer, administrator, or owner.</param>
public sealed record SetTenantMembershipRequest(string Role);

/// <summary>
/// Returns one tenant membership.
/// </summary>
/// <param name="User">Member GitHub identity.</param>
/// <param name="Role">Role granted within the tenant.</param>
/// <param name="CreatedAt">Time the membership was created.</param>
public sealed record TenantMemberResponse(
    DashboardUserResponse User,
    string Role,
    DateTimeOffset CreatedAt);

/// <summary>
/// Requests one scoped, expiring diagnostic credential.
/// </summary>
/// <param name="Label">Operator-facing purpose.</param>
/// <param name="ExpiresAt">Time after which authentication fails.</param>
/// <param name="NodeIds">Allowed nodes, or empty for all tenant nodes.</param>
/// <param name="ProfileIds">Allowed profiles, or empty for all profiles.</param>
public sealed record CreateDiagnosticCredentialRequest(
    string Label,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<Guid>? NodeIds,
    IReadOnlyList<string>? ProfileIds);

/// <summary>
/// Returns non-secret diagnostic credential metadata.
/// </summary>
/// <param name="CredentialId">Dashboard-assigned credential identifier.</param>
/// <param name="Label">Operator-facing purpose.</param>
/// <param name="CreatedByGitHubUserId">Administrator that created the credential.</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="ExpiresAt">Expiry time.</param>
/// <param name="RevokedAt">Revocation time when inactive.</param>
/// <param name="RevokedByGitHubUserId">Administrator that revoked the credential.</param>
/// <param name="RotatedFromCredentialId">Credential replaced by this credential.</param>
/// <param name="LastUsedAt">Most recent successful authentication time.</param>
/// <param name="UseCount">Successful authentication count.</param>
/// <param name="NodeIds">Allowed nodes, or empty for all tenant nodes.</param>
/// <param name="ProfileIds">Allowed profiles, or empty for all profiles.</param>
public sealed record DiagnosticCredentialResponse(
    Guid CredentialId,
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
/// Returns a raw diagnostic credential exactly once with its metadata.
/// </summary>
/// <param name="Credential">Non-secret credential metadata.</param>
/// <param name="Value">Raw credential that is never stored recoverably.</param>
public sealed record DiagnosticCredentialCreatedResponse(
    DiagnosticCredentialResponse Credential,
    string Value);
