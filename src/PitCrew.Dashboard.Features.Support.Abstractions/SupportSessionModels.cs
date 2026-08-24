using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support.Abstractions;

/// <summary>
/// Defines the bounded support diagnostic request accepted by Dashboard APIs.
/// </summary>
/// <param name="NodeId">Target support node identifier.</param>
/// <param name="DiagnosticMode">Closed diagnostic mode.</param>
/// <param name="ProfileId">Optional locally configured PitCrew profile identifier.</param>
/// <param name="ExpiresInSeconds">Requested lifetime in seconds.</param>
public sealed record SupportDiagnosticSessionInput(
    Guid NodeId,
    string DiagnosticMode,
    string? ProfileId,
    int ExpiresInSeconds);

/// <summary>
/// Persists one Dashboard-authorized support diagnostic session.
/// </summary>
/// <param name="TenantId">Tenant that owns the session.</param>
/// <param name="SessionId">Dashboard-assigned session identifier.</param>
/// <param name="NodeId">Target support node identifier.</param>
/// <param name="DiagnosticMode">Closed diagnostic mode.</param>
/// <param name="ProfileId">Optional locally configured PitCrew profile identifier.</param>
/// <param name="PackageId">Stable package identifier sent to the local broker.</param>
/// <param name="Capability">Support capability authorized at creation.</param>
/// <param name="RequestDigest">Lowercase SHA-256 digest of the canonical request payload.</param>
/// <param name="NodeSigningKeyFingerprint">Lowercase SHA-256 fingerprint of the enrolled node signing SPKI.</param>
/// <param name="Status">Session lifecycle state.</param>
/// <param name="RequestedByGitHubUserId">Dashboard actor or diagnostic credential identifier.</param>
/// <param name="RequestedAt">Dashboard authorization time.</param>
/// <param name="ExpiresAt">Time the node must reject the request.</param>
/// <param name="RequestEnvelope">Opaque sealed request envelope queued through the relay.</param>
/// <param name="DispatchedAt">First relay dispatch time when observed.</param>
/// <param name="RejectionDisposition">Closed agent rejection disposition.</param>
/// <param name="CompletedAt">Completion time when terminal.</param>
/// <param name="ResultEnvelope">Opaque sealed result envelope uploaded through the relay.</param>
/// <param name="Report">Verified report JSON after Dashboard decryption.</param>
/// <param name="Markdown">Verified markdown after Dashboard decryption.</param>
/// <param name="Attestation">Detached node-signature attestation for operator skills.</param>
public sealed record SupportDiagnosticSession(
    string TenantId,
    Guid SessionId,
    Guid NodeId,
    string DiagnosticMode,
    string? ProfileId,
    string PackageId,
    string Capability,
    string RequestDigest,
    string NodeSigningKeyFingerprint,
    SupportDiagnosticSessionStatus Status,
    string RequestedByGitHubUserId,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    SupportEnvelope RequestEnvelope,
    DateTimeOffset? DispatchedAt,
    string? RejectionDisposition,
    DateTimeOffset? CompletedAt,
    SupportEnvelope? ResultEnvelope,
    JsonElement? Report,
    string? Markdown,
    SupportResultAttestation? Attestation);

/// <summary>
/// Result of creating or mutating a support diagnostic session.
/// </summary>
public enum SupportMutationStatus
{
  /// <summary>The mutation completed.</summary>
  Succeeded,

  /// <summary>The requested resource does not exist in the tenant.</summary>
  NotFound,

  /// <summary>The target identity is revoked.</summary>
  Revoked,

  /// <summary>The request was invalid or unsupported.</summary>
  Invalid,

  /// <summary>The caller is not allowed to act on this support resource.</summary>
  Forbidden,

  /// <summary>The session is already terminal or otherwise conflicts.</summary>
  Conflict,
}

/// <summary>
/// Result returned by support session creation.
/// </summary>
/// <param name="Status">Creation status.</param>
/// <param name="Error">Stable error message for invalid requests.</param>
/// <param name="Session">Created session when successful.</param>
public sealed record SupportSessionMutation(
    SupportMutationStatus Status,
    string? Error,
    SupportDiagnosticSession? Session);
