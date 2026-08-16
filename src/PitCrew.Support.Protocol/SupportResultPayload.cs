using System.Text.Json;

namespace PitCrew.Support.Protocol;

/// <summary>
/// Diagnostic result content signed by a node and decrypted by Dashboard.
/// </summary>
/// <param name="TenantId">Tenant that owns the diagnostic session.</param>
/// <param name="NodeId">Support node that produced the result.</param>
/// <param name="SessionId">Dashboard diagnostic session identifier.</param>
/// <param name="Capability">Support capability authorized at session creation.</param>
/// <param name="RequestDigest">Lowercase SHA-256 digest of the canonical request payload.</param>
/// <param name="ExpiresAt">Request expiry pinned at session creation.</param>
/// <param name="Report">Structured report JSON produced by the file-only broker.</param>
/// <param name="Markdown">Human-readable markdown summary produced by the file-only broker.</param>
public sealed record SupportResultPayload(
    string TenantId,
    Guid NodeId,
    Guid SessionId,
    string Capability,
    string RequestDigest,
    DateTimeOffset ExpiresAt,
    JsonElement Report,
    string Markdown);

/// <summary>
/// Detached attestation returned to PitCrew operator skills with completed support results.
/// </summary>
/// <param name="NodeSigningPublicKeySpki">Node ECDSA public key as base64 SPKI.</param>
/// <param name="PayloadBase64Url">Canonical UTF-8 JSON payload.</param>
/// <param name="SignatureBase64Url">ECDSA P-256 IEEE-P1363 signature.</param>
/// <param name="SignatureAlgorithm">Signature algorithm identifier.</param>
public sealed record SupportResultAttestation(
    string NodeSigningPublicKeySpki,
    string PayloadBase64Url,
    string SignatureBase64Url,
    string SignatureAlgorithm);
