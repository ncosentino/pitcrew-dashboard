namespace PitCrew.Dashboard.Features.Support.Abstractions;

/// <summary>
/// Describes one independently revocable support-agent identity.
/// </summary>
/// <param name="TenantId">Tenant that owns the support identity.</param>
/// <param name="NodeId">Dashboard-assigned support node identifier.</param>
/// <param name="DisplayName">Operator-facing node display name.</param>
/// <param name="NodeSigningPublicKeySpki">ECDSA P-256 public key used to verify node results.</param>
/// <param name="NodeEncryptionPublicKeySpki">RSA 3072 public key used to encrypt diagnostic requests.</param>
/// <param name="CreatedByGitHubUserId">Administrator that enrolled the identity.</param>
/// <param name="CreatedAt">Time the identity was enrolled.</param>
/// <param name="RevokedAt">Revocation time when inactive.</param>
/// <param name="RevokedByGitHubUserId">Administrator that revoked the identity.</param>
/// <param name="LastPollAt">Most recent successful support-agent poll.</param>
/// <param name="LastResultAt">Most recent successful support-agent result upload.</param>
/// <param name="CapabilityVersion">Highest support diagnostic capability version advertised by the node.</param>
public sealed record SupportIdentity(
    string TenantId,
    Guid NodeId,
    string DisplayName,
    string NodeSigningPublicKeySpki,
    string NodeEncryptionPublicKeySpki,
    string CreatedByGitHubUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    string? RevokedByGitHubUserId,
    DateTimeOffset? LastPollAt,
    DateTimeOffset? LastResultAt,
    int CapabilityVersion)
{
  /// <summary>
  /// Gets the Dashboard support availability state.
  /// </summary>
  public SupportIdentityStatus Status =>
      RevokedAt is null ? SupportIdentityStatus.Active : SupportIdentityStatus.Revoked;
}

/// <summary>
/// Carries a new support identity and hashed transport credential into storage.
/// </summary>
/// <param name="Identity">Support identity metadata.</param>
/// <param name="TransportCredentialHash">One-way hash of the relay bearer credential.</param>
/// <param name="EnrollmentCodeHash">One-way hash of the one-time tenant-bound enrollment code.</param>
/// <param name="EnrollmentExpiresAt">Time after which the enrollment code is unusable.</param>
public sealed record SupportIdentityWrite(
    SupportIdentity Identity,
    string TransportCredentialHash,
    string EnrollmentCodeHash,
    DateTimeOffset EnrollmentExpiresAt);

/// <summary>
/// Result returned when a node completes one-time support enrollment.
/// </summary>
/// <param name="Identity">Created support identity.</param>
/// <param name="TransportCredential">High-entropy relay bearer credential returned once.</param>
/// <param name="RelayUrl">Relay base URL the support agent should poll.</param>
/// <param name="AuthorizationSigningPublicKeySpki">Dashboard ECDSA public key pinned by the node.</param>
/// <param name="ResultEncryptionPublicKeySpki">Dashboard RSA public key used by the node to encrypt results.</param>
public sealed record CreatedSupportEnrollment(
    SupportIdentity Identity,
    string TransportCredential,
    string RelayUrl,
    string AuthorizationSigningPublicKeySpki,
    string ResultEncryptionPublicKeySpki);
