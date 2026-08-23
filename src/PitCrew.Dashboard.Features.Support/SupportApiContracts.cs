using System.Text.Json;

namespace PitCrew.Dashboard.Features.Support;

/// <summary>
/// Legacy request to create a support identity from manually supplied public keys.
/// </summary>
/// <param name="DisplayName">Operator-facing support node label.</param>
/// <param name="NodeSigningPublicKeySpki">Node ECDSA public key as base64url SPKI.</param>
/// <param name="NodeEncryptionPublicKeySpki">Node RSA public key as base64url SPKI.</param>
public sealed record CreateSupportEnrollmentRequest(
    string DisplayName,
    string NodeSigningPublicKeySpki,
    string NodeEncryptionPublicKeySpki);

/// <summary>
/// Legacy response containing support identity metadata and one-time secret material.
/// </summary>
/// <param name="NodeId">Dashboard-assigned support node identifier.</param>
/// <param name="DisplayName">Operator-facing support node label.</param>
/// <param name="EnrollmentCode">One-time tenant-bound enrollment code.</param>
/// <param name="TransportCredential">Relay bearer credential returned once.</param>
/// <param name="EnrollmentExpiresAt">Enrollment expiry time.</param>
/// <param name="RelayUrl">Relay base URL.</param>
/// <param name="AuthorizationSigningPublicKeySpki">Dashboard request-signing public SPKI.</param>
/// <param name="ResultEncryptionPublicKeySpki">Dashboard result-encryption public SPKI.</param>
public sealed record CreatedSupportEnrollmentResponse(
    string NodeId,
    string DisplayName,
    string EnrollmentCode,
    string TransportCredential,
    DateTimeOffset EnrollmentExpiresAt,
    string RelayUrl,
    string AuthorizationSigningPublicKeySpki,
    string ResultEncryptionPublicKeySpki);

/// <summary>
/// Request to create one tenant-bound support enrollment authorization.
/// </summary>
/// <param name="DisplayName">Operator-facing support node label.</param>
public sealed record CreateSupportEnrollmentAuthorizationRequest(
    string DisplayName);

/// <summary>
/// Response containing one-time tenant-bound enrollment material.
/// </summary>
/// <param name="DisplayName">Operator-facing support node label.</param>
/// <param name="EnrollmentCode">One-time tenant-bound enrollment code.</param>
/// <param name="EnrollmentExpiresAt">Enrollment expiry time.</param>
public sealed record CreatedSupportEnrollmentAuthorizationResponse(
    string DisplayName,
    string EnrollmentCode,
    DateTimeOffset EnrollmentExpiresAt);

/// <summary>
/// Request submitted by a node to complete one-time support enrollment.
/// </summary>
/// <param name="TenantId">Tenant bound to the one-time enrollment.</param>
/// <param name="EnrollmentCode">One-time enrollment code.</param>
/// <param name="CompletionId">Stable node-generated identifier for exact retry recovery.</param>
/// <param name="NodeSigningPublicKeySpki">Locally generated ECDSA P-256 public SPKI.</param>
/// <param name="NodeEncryptionPublicKeySpki">Locally generated RSA-3072 public SPKI.</param>
public sealed record CompleteSupportEnrollmentRequest(
    string TenantId,
    string EnrollmentCode,
    Guid CompletionId,
    string NodeSigningPublicKeySpki,
    string NodeEncryptionPublicKeySpki);

/// <summary>
/// Request to atomically rotate one active support identity.
/// </summary>
/// <param name="RotationId">Node-generated idempotency identifier.</param>
/// <param name="TenantId">Tenant that owns the support node.</param>
/// <param name="CurrentTransportCredential">Current relay credential authorizing rotation.</param>
/// <param name="ReplacementTransportCredential">Locally staged replacement relay credential.</param>
/// <param name="NodeSigningPublicKeySpki">Staged ECDSA P-256 public SPKI.</param>
/// <param name="NodeEncryptionPublicKeySpki">Staged RSA-3072 public SPKI.</param>
public sealed record RotateSupportIdentityRequest(
    Guid RotationId,
    string TenantId,
    string CurrentTransportCredential,
    string ReplacementTransportCredential,
    string NodeSigningPublicKeySpki,
    string NodeEncryptionPublicKeySpki);

/// <summary>
/// Request to finalize one prepared support identity rotation.
/// </summary>
/// <param name="RotationId">Node-generated rotation identifier.</param>
/// <param name="TenantId">Tenant that owns the support node.</param>
/// <param name="CurrentTransportCredential">
/// Locally committed replacement relay credential.
/// </param>
public sealed record FinalizeSupportIdentityRotationRequest(
    Guid RotationId,
    string TenantId,
    string CurrentTransportCredential);

/// <summary>
/// Support identity metadata safe for Dashboard display.
/// </summary>
public sealed record SupportIdentityResponse(
    string NodeId,
    string DisplayName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastPollAt,
    DateTimeOffset? LastResultAt,
    int CapabilityVersion);

/// <summary>
/// Request to create one bounded read-only diagnostic session.
/// </summary>
/// <param name="NodeId">Target support node identifier.</param>
/// <param name="DiagnosticMode">Closed v1 diagnostic mode.</param>
/// <param name="ProfileId">Optional locally configured PitCrew profile identifier.</param>
/// <param name="ExpiresInSeconds">Requested session lifetime.</param>
public sealed record CreateSupportDiagnosticSessionRequest(
    Guid NodeId,
    string DiagnosticMode,
    string? ProfileId,
    int ExpiresInSeconds);

/// <summary>
/// Response for one support diagnostic session.
/// </summary>
public sealed record SupportDiagnosticSessionResponse(
    string SessionId,
    string NodeId,
    string DiagnosticMode,
    string? ProfileId,
    string Capability,
    string RequestDigest,
    string NodeSigningKeyFingerprint,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    SupportDiagnosticResultResponse? Result);

/// <summary>
/// Verified support diagnostic result returned only for completed sessions.
/// </summary>
public sealed record SupportDiagnosticResultResponse(
    JsonElement Report,
    string Markdown,
    SupportDiagnosticAttestationResponse Attestation);

/// <summary>
/// Node-signature attestation returned with completed diagnostic sessions.
/// </summary>
public sealed record SupportDiagnosticAttestationResponse(
    string NodeSigningPublicKeySpki,
    string PayloadBase64Url,
    string SignatureBase64Url,
    string SignatureAlgorithm);
