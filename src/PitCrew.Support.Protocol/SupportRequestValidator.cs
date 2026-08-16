namespace PitCrew.Support.Protocol;

/// <summary>
/// Validates authenticated support diagnostic requests after decryption.
/// </summary>
public static class SupportRequestValidator
{
  /// <summary>
  /// Validates v1 tenant, node, capability, expiry, nonce, and diagnostic-mode constraints.
  /// </summary>
  /// <param name="request">Request to validate.</param>
  /// <param name="tenantId">Expected tenant.</param>
  /// <param name="nodeId">Expected support node.</param>
  /// <param name="now">Node-local time.</param>
  /// <param name="nonceWasSeen">Function that returns whether the nonce is already tombstoned.</param>
  /// <returns>Validation status.</returns>
  public static SupportRequestValidationStatus Validate(
      SupportDiagnosticRequest request,
      string tenantId,
      Guid nodeId,
      DateTimeOffset now,
      Func<string, bool> nonceWasSeen)
  {
    ArgumentNullException.ThrowIfNull(nonceWasSeen);
    if (!string.Equals(request.ProtocolVersion, "support-plane-v1", StringComparison.Ordinal) ||
        !string.Equals(request.CapabilityName, SupportCapability.DiagnosticsSnapshotV1, StringComparison.Ordinal) ||
        request.CapabilityVersion != 1)
    {
      return SupportRequestValidationStatus.UnsupportedCapability;
    }
    if (!string.Equals(request.TenantId, tenantId, StringComparison.Ordinal) ||
        request.NodeId != nodeId)
    {
      return SupportRequestValidationStatus.WrongTenantOrNode;
    }
    if (!SupportDiagnosticModes.IsSupported(request.DiagnosticMode))
    {
      return SupportRequestValidationStatus.UnsupportedDiagnosticMode;
    }
    if (request.ExpiresAt <= now || request.IssuedAt > now.AddMinutes(5))
    {
      return SupportRequestValidationStatus.Expired;
    }
    if (string.IsNullOrWhiteSpace(request.Nonce) || request.Nonce.Length < 32)
    {
      return SupportRequestValidationStatus.InvalidNonce;
    }
    return nonceWasSeen(request.Nonce)
        ? SupportRequestValidationStatus.Replay
        : SupportRequestValidationStatus.Valid;
  }
}

/// <summary>
/// Diagnostic request validation outcomes used by the node agent.
/// </summary>
public enum SupportRequestValidationStatus
{
  /// <summary>The request is valid.</summary>
  Valid,

  /// <summary>The request targets another tenant or node.</summary>
  WrongTenantOrNode,

  /// <summary>The capability or protocol version is unsupported.</summary>
  UnsupportedCapability,

  /// <summary>The diagnostic mode is not in the closed v1 allowlist.</summary>
  UnsupportedDiagnosticMode,

  /// <summary>The request is stale or not yet valid within the allowed skew.</summary>
  Expired,

  /// <summary>The nonce is missing or malformed.</summary>
  InvalidNonce,

  /// <summary>The nonce has already been processed.</summary>
  Replay,
}
