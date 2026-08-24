using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed record SupportRelaySessionState(
    string TenantId,
    Guid NodeId,
    Guid SessionId,
    SupportDiagnosticSessionStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? RejectedAt,
    string? RejectionDisposition);
