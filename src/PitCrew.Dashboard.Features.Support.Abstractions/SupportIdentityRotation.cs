namespace PitCrew.Dashboard.Features.Support.Abstractions;

/// <summary>
/// Carries an interruption-safe support identity rotation into storage.
/// </summary>
/// <param name="RotationId">Node-generated identifier shared across exact retries.</param>
/// <param name="TenantId">Tenant that owns the support node.</param>
/// <param name="NodeId">Support node identifier.</param>
/// <param name="ExpectedTransportCredentialHash">Current credential hash authorizing rotation.</param>
/// <param name="ReplacementTransportCredentialHash">Replacement credential hash.</param>
/// <param name="NodeSigningPublicKeySpki">Replacement ECDSA P-256 public SPKI.</param>
/// <param name="NodeEncryptionPublicKeySpki">Replacement RSA-3072 public SPKI.</param>
public sealed record SupportIdentityRotation(
    Guid RotationId,
    string TenantId,
    Guid NodeId,
    string ExpectedTransportCredentialHash,
    string ReplacementTransportCredentialHash,
    string NodeSigningPublicKeySpki,
    string NodeEncryptionPublicKeySpki);

/// <summary>
/// Durable phase of a prepared support identity rotation.
/// </summary>
public enum SupportIdentityRotationPhase
{
  /// <summary>Relay accepts both credentials while Dashboard retains old active keys.</summary>
  Prepared,

  /// <summary>Dashboard uses replacement keys while relay still accepts both credentials.</summary>
  DashboardPromoted,

  /// <summary>Relay retired the old credential and the replacement is fully active.</summary>
  Finalized,
}

/// <summary>
/// Describes one durable support identity rotation.
/// </summary>
/// <param name="Rotation">Requested rotation values.</param>
/// <param name="Phase">Current durable phase.</param>
/// <param name="CreatedAt">Preparation time.</param>
/// <param name="DashboardPromotedAt">Time Dashboard activated replacement keys.</param>
/// <param name="FinalizedAt">Time relay retirement completed.</param>
public sealed record StoredSupportIdentityRotation(
    SupportIdentityRotation Rotation,
    SupportIdentityRotationPhase Phase,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DashboardPromotedAt,
    DateTimeOffset? FinalizedAt);

/// <summary>
/// Describes whether a support identity rotation may proceed or already completed.
/// </summary>
public enum SupportIdentityRotationStatus
{
  /// <summary>The current credential authorizes the requested replacement.</summary>
  Authorized,

  /// <summary>The requested replacement is already the active identity.</summary>
  AlreadyApplied,

  /// <summary>The relay and Dashboard durably prepared the exact replacement.</summary>
  Prepared,

  /// <summary>Dashboard promoted replacement keys while relay still accepts both credentials.</summary>
  DashboardPromoted,

  /// <summary>The replacement is fully active and the old credential is retired.</summary>
  Finalized,

  /// <summary>The support node does not exist in the tenant.</summary>
  NotFound,

  /// <summary>The support node is revoked.</summary>
  Revoked,

  /// <summary>The supplied credential does not authorize the rotation.</summary>
  Forbidden,

  /// <summary>An active diagnostic session prevents safe key replacement.</summary>
  ActiveSessions,

  /// <summary>A different rotation is already pending for the node.</summary>
  Conflict,
}
