using System.Text.Json;

namespace PitCrew.Dashboard.Features.Support;

/// <summary>
/// Request to create a support identity and enrollment material.
/// </summary>
/// <param name="DisplayName">Operator-facing support node label.</param>
/// <param name="NodeSigningPublicKeySpki">Node ECDSA public key as base64url SPKI.</param>
/// <param name="NodeEncryptionPublicKeySpki">Node RSA public key as base64url SPKI.</param>
public sealed record CreateSupportEnrollmentRequest(
    string DisplayName,
    string NodeSigningPublicKeySpki,
    string NodeEncryptionPublicKeySpki);

/// <summary>
/// Response containing support identity metadata and one-time secret material.
/// </summary>
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
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    string? NodeSigningKeyFingerprint,
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
