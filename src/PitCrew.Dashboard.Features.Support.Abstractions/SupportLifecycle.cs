namespace PitCrew.Dashboard.Features.Support.Abstractions;

/// <summary>
/// Lifecycle states for read-only support diagnostic sessions.
/// </summary>
public enum SupportDiagnosticSessionStatus
{
  /// <summary>The request exists in Dashboard but has not been picked up by a node.</summary>
  Queued,

  /// <summary>The relay dispatched the opaque request envelope to the target node.</summary>
  Dispatched,

  /// <summary>The node returned a verified encrypted diagnostic result.</summary>
  Completed,

  /// <summary>The node rejected the request without running diagnostics.</summary>
  Rejected,

  /// <summary>The operator cancelled the session before completion.</summary>
  Cancelled,

  /// <summary>The session expired before a terminal node result arrived.</summary>
  Expired,
}

/// <summary>
/// Dashboard-visible support availability distinct from normal connector health.
/// </summary>
public enum SupportIdentityStatus
{
  /// <summary>The support identity can poll for read-only diagnostics.</summary>
  Active,

  /// <summary>The support identity is explicitly revoked.</summary>
  Revoked,
}

/// <summary>
/// Closed reason categories reported by support diagnostics.
/// </summary>
public enum SupportClosedMode
{
  /// <summary>The normal connector is offline or stale.</summary>
  ConnectorOffline,

  /// <summary>Observed capacity does not match the requested or configured state.</summary>
  CapacityMismatch,

  /// <summary>A job was expected but not assigned to the node.</summary>
  JobNotAssigned,

  /// <summary>The host reports local pressure evidence.</summary>
  HostPressure,

  /// <summary>Collect all v1 file-only diagnostic evidence.</summary>
  Full,
}
