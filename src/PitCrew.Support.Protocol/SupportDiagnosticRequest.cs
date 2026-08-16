namespace PitCrew.Support.Protocol;

/// <summary>
/// Canonical read-only diagnostic capability request authorized by Dashboard.
/// </summary>
/// <param name="ProtocolVersion">Support protocol version. V1 uses <c>support-plane-v1</c>.</param>
/// <param name="TenantId">Tenant that owns the support identity.</param>
/// <param name="NodeId">Dashboard-assigned support node identifier.</param>
/// <param name="SessionId">Dashboard-assigned diagnostic session identifier.</param>
/// <param name="CapabilityName">Capability name. V1 uses <c>pitcrew.diagnostics.snapshot.v1</c>.</param>
/// <param name="CapabilityVersion">Capability version. V1 uses <c>1</c>.</param>
/// <param name="DiagnosticMode">Closed diagnostic mode requested by the operator.</param>
/// <param name="ProfileId">Optional locally configured PitCrew profile identifier.</param>
/// <param name="PackageId">Stable package identifier carried into the local collector.</param>
/// <param name="IssuedAt">Dashboard authorization time.</param>
/// <param name="ExpiresAt">Time after which the node rejects the request.</param>
/// <param name="Nonce">High-entropy request nonce used by the node replay cache.</param>
public sealed record SupportDiagnosticRequest(
    string ProtocolVersion,
    string TenantId,
    Guid NodeId,
    Guid SessionId,
    string CapabilityName,
    int CapabilityVersion,
    string DiagnosticMode,
    string? ProfileId,
    string PackageId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Nonce);
