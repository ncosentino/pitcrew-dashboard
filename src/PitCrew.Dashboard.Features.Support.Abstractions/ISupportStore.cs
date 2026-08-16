namespace PitCrew.Dashboard.Features.Support.Abstractions;

/// <summary>
/// Persists Dashboard-owned support identities, sessions, and audit records.
/// </summary>
public interface ISupportStore
{
  /// <summary>
  /// Creates a tenant-bound one-time support enrollment.
  /// </summary>
  /// <param name="enrollment">Enrollment authorization to persist.</param>
  /// <param name="cancellationToken">Token that cancels the write.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> CreateEnrollmentAsync(
      SupportEnrollment enrollment,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads an enrollment by tenant and hashed one-time code.
  /// </summary>
  /// <param name="tenantId">Tenant expected to own the enrollment.</param>
  /// <param name="enrollmentCodeHash">One-way hash of the supplied code.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The enrollment, including completion recovery state, or <see langword="null" /> when absent.</returns>
  Task<SupportEnrollment?> GetEnrollmentOrNullAsync(
      string tenantId,
      string enrollmentCodeHash,
      CancellationToken cancellationToken);

  /// <summary>
  /// Deletes expired unused enrollments and consumed recovery envelopes.
  /// </summary>
  /// <param name="now">Current time used for expiry comparison.</param>
  /// <param name="limit">Maximum rows deleted in one call.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  Task PurgeExpiredEnrollmentsAsync(
      DateTimeOffset now,
      int limit,
      CancellationToken cancellationToken);

  /// <summary>
  /// Atomically consumes one enrollment and creates its support identity.
  /// </summary>
  /// <param name="enrollmentId">Enrollment being consumed.</param>
  /// <param name="completionId">Node-generated identifier that makes an exact completion retry recoverable.</param>
  /// <param name="write">Identity and secret hashes to store.</param>
  /// <param name="transportCredentialEnvelope">Transport credential encrypted to the node public key.</param>
  /// <param name="consumedAt">Completion time used for expiry and replay checks.</param>
  /// <param name="recoveryExpiresAt">End of the exact retry recovery window.</param>
  /// <param name="relayCleanupLeaseId">Lease that protects the in-flight relay registration.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> CompleteEnrollmentAsync(
      Guid enrollmentId,
      Guid completionId,
      SupportIdentityWrite write,
      PitCrew.Support.Protocol.SupportEnvelope transportCredentialEnvelope,
      DateTimeOffset consumedAt,
      DateTimeOffset recoveryExpiresAt,
      Guid relayCleanupLeaseId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Queues durable relay cleanup after an enrollment registration cannot commit.
  /// </summary>
  /// <param name="nodeId">Potentially orphaned relay node.</param>
  /// <param name="createdAt">Time the external registration begins.</param>
  /// <param name="leaseId">Lease owned by the enrollment operation.</param>
  /// <param name="leaseExpiresAt">Time after which maintenance may reclaim cleanup.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  Task QueueRelayCleanupAsync(
      Guid nodeId,
      DateTimeOffset createdAt,
      Guid leaseId,
      DateTimeOffset leaseExpiresAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Atomically leases bounded due relay cleanup work.
  /// </summary>
  /// <param name="now">Current time used for due and expired-lease checks.</param>
  /// <param name="leaseId">Lease assigned to claimed records.</param>
  /// <param name="leaseExpiresAt">Lease expiry.</param>
  /// <param name="limit">Maximum cleanup records leased.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Claimed cleanup records in deterministic order.</returns>
  Task<IReadOnlyList<SupportRelayCleanup>> ClaimRelayCleanupAsync(
      DateTimeOffset now,
      Guid leaseId,
      DateTimeOffset leaseExpiresAt,
      int limit,
      CancellationToken cancellationToken);

  /// <summary>
  /// Records an attempt against cleanup held by the specified lease.
  /// </summary>
  /// <param name="nodeId">Relay node being cleaned up.</param>
  /// <param name="leaseId">Lease that owns the cleanup.</param>
  /// <param name="attemptedAt">Attempt time.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns><see langword="true" /> when the lease still owns the record.</returns>
  Task<bool> RecordRelayCleanupAttemptAsync(
      Guid nodeId,
      Guid leaseId,
      DateTimeOffset attemptedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Defers failed cleanup with a new due time and releases its lease.
  /// </summary>
  /// <param name="nodeId">Relay node whose cleanup failed.</param>
  /// <param name="leaseId">Lease that owns the cleanup.</param>
  /// <param name="nextAttemptAt">Earliest next retry time.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns><see langword="true" /> when the lease still owned the record.</returns>
  Task<bool> DeferRelayCleanupAsync(
      Guid nodeId,
      Guid leaseId,
      DateTimeOffset nextAttemptAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Removes completed relay cleanup work held by the specified lease.
  /// </summary>
  /// <param name="nodeId">Relay node whose cleanup completed.</param>
  /// <param name="leaseId">Lease that owns the cleanup.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns><see langword="true" /> when the lease still owned the record.</returns>
  Task<bool> CompleteRelayCleanupAsync(
      Guid nodeId,
      Guid leaseId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Creates one support identity with one-time enrollment and relay credential hashes.
  /// </summary>
  /// <param name="write">Identity and secret hashes to store.</param>
  /// <param name="cancellationToken">Token that cancels the write.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> CreateIdentityAsync(
      SupportIdentityWrite write,
      CancellationToken cancellationToken);

  /// <summary>
  /// Lists support identities for one tenant.
  /// </summary>
  /// <param name="tenantId">Tenant whose identities are returned.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Tenant identities ordered by creation time.</returns>
  Task<IReadOnlyList<SupportIdentity>> GetIdentitiesAsync(
      string tenantId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads one tenant-owned support identity.
  /// </summary>
  /// <param name="tenantId">Tenant that should own the node.</param>
  /// <param name="nodeId">Support node identifier.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The identity, or <see langword="null" /> when it does not exist.</returns>
  Task<SupportIdentity?> GetIdentityOrNullAsync(
      string tenantId,
      Guid nodeId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Revokes one support identity and prevents future polls.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the node.</param>
  /// <param name="nodeId">Support node identifier.</param>
  /// <param name="revokedByGitHubUserId">Administrator that revoked the identity.</param>
  /// <param name="revokedAt">Revocation time.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> RevokeIdentityAsync(
      string tenantId,
      Guid nodeId,
      string revokedByGitHubUserId,
      DateTimeOffset revokedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Checks whether the current or replacement transport credential authorizes a rotation.
  /// </summary>
  /// <param name="rotation">Requested key and credential replacement.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Typed authorization and idempotency status.</returns>
  Task<SupportIdentityRotationStatus> GetIdentityRotationStatusAsync(
      SupportIdentityRotation rotation,
      CancellationToken cancellationToken);

  /// <summary>
  /// Gets one durable support identity rotation.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the support node.</param>
  /// <param name="nodeId">Support node identifier.</param>
  /// <param name="rotationId">Node-generated rotation identifier.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>The stored rotation, or <see langword="null"/>.</returns>
  Task<StoredSupportIdentityRotation?> GetIdentityRotationOrNullAsync(
      string tenantId,
      Guid nodeId,
      Guid rotationId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Durably prepares replacement keys while leaving the current identity active.
  /// </summary>
  /// <param name="rotation">Prepared replacement values.</param>
  /// <param name="createdAt">Preparation time.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> PrepareIdentityRotationAsync(
      SupportIdentityRotation rotation,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Atomically activates prepared Dashboard keys while relay still accepts both credentials.
  /// </summary>
  /// <param name="rotation">Prepared replacement values.</param>
  /// <param name="promotedAt">Dashboard promotion time.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> PromoteIdentityRotationAsync(
      SupportIdentityRotation rotation,
      DateTimeOffset promotedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Marks relay retirement complete and unblocks support session creation.
  /// </summary>
  /// <param name="rotation">Promoted replacement values.</param>
  /// <param name="finalizedAt">Relay promotion completion time.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> FinalizeIdentityRotationAsync(
      SupportIdentityRotation rotation,
      DateTimeOffset finalizedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Creates one queued diagnostic session.
  /// </summary>
  /// <param name="session">Session to persist.</param>
  /// <param name="expectedNodeSigningPublicKeySpki">Signing key used to pin the session.</param>
  /// <param name="expectedNodeEncryptionPublicKeySpki">Encryption key used to seal the request.</param>
  /// <param name="cancellationToken">Token that cancels the write.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> CreateSessionAsync(
      SupportDiagnosticSession session,
      string expectedNodeSigningPublicKeySpki,
      string expectedNodeEncryptionPublicKeySpki,
      CancellationToken cancellationToken);

  /// <summary>
  /// Gets one tenant diagnostic session.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the session.</param>
  /// <param name="sessionId">Session identifier.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The session, or <see langword="null" /> when it does not exist.</returns>
  Task<SupportDiagnosticSession?> GetSessionOrNullAsync(
      string tenantId,
      Guid sessionId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Lists recent support diagnostic sessions for one tenant.
  /// </summary>
  /// <param name="tenantId">Tenant whose sessions are returned.</param>
  /// <param name="limit">Maximum number of sessions to return.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Recent sessions ordered by request time descending.</returns>
  Task<IReadOnlyList<SupportDiagnosticSession>> GetSessionsAsync(
      string tenantId,
      int limit,
      CancellationToken cancellationToken);

  /// <summary>
  /// Cancels a queued or dispatched diagnostic session.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the session.</param>
  /// <param name="sessionId">Session identifier.</param>
  /// <param name="cancelledAt">Cancellation time.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> CancelSessionAsync(
      string tenantId,
      Guid sessionId,
      DateTimeOffset cancelledAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Stores a verified terminal result after Dashboard decryption.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the session.</param>
  /// <param name="sessionId">Session identifier.</param>
  /// <param name="result">Opaque result envelope from the relay.</param>
  /// <param name="reportJson">Verified report JSON.</param>
  /// <param name="markdown">Verified markdown.</param>
  /// <param name="attestationJson">Detached attestation JSON.</param>
  /// <param name="completedAt">Completion time.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> CompleteSessionAsync(
      string tenantId,
      Guid sessionId,
      string result,
      string reportJson,
      string markdown,
      string attestationJson,
      DateTimeOffset completedAt,
      CancellationToken cancellationToken);
}
