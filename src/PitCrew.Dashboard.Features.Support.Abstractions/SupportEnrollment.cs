using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Features.Support.Abstractions;

/// <summary>
/// Describes one tenant-bound, one-time support enrollment authorization.
/// </summary>
/// <param name="EnrollmentId">Stable enrollment identifier used for atomic completion.</param>
/// <param name="TenantId">Tenant that owns the enrollment.</param>
/// <param name="DisplayName">Operator-facing label assigned to the completing node.</param>
/// <param name="EnrollmentCodeHash">One-way hash of the enrollment code.</param>
/// <param name="CreatedByGitHubUserId">Administrator that created the enrollment.</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="ExpiresAt">Time after which completion is rejected.</param>
/// <param name="ConsumedAt">Completion time, or <see langword="null" /> while unused.</param>
/// <param name="RecoveryExpiresAt">End of the exact completion-recovery window.</param>
/// <param name="CompletionId">Node-generated completion identifier used for exact retry recovery.</param>
/// <param name="CompletedNodeId">Created support node identifier.</param>
/// <param name="TransportCredentialEnvelope">Credential encrypted to the completing node.</param>
public sealed record SupportEnrollment(
    Guid EnrollmentId,
    string TenantId,
    string DisplayName,
    string EnrollmentCodeHash,
    string CreatedByGitHubUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    DateTimeOffset? RecoveryExpiresAt,
    Guid? CompletionId,
    Guid? CompletedNodeId,
    SupportEnvelope? TransportCredentialEnvelope);

/// <summary>
/// Describes relay registrations that require durable cleanup after enrollment failure.
/// </summary>
/// <param name="NodeId">Relay node identifier to revoke.</param>
/// <param name="CreatedAt">Time cleanup was queued.</param>
/// <param name="LastAttemptAt">Most recent cleanup attempt.</param>
/// <param name="AttemptCount">Number of attempted revocations.</param>
/// <param name="NextAttemptAt">Earliest next retry time.</param>
/// <param name="LeaseId">Current cleanup lease.</param>
/// <param name="LeaseExpiresAt">Current lease expiry.</param>
public sealed record SupportRelayCleanup(
    Guid NodeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    Guid LeaseId,
    DateTimeOffset LeaseExpiresAt);
