namespace PitCrew.Dashboard.Features.Support.Abstractions;

/// <summary>
/// Persists Dashboard-owned support identities, sessions, and audit records.
/// </summary>
public interface ISupportStore
{
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
  /// Creates one queued diagnostic session.
  /// </summary>
  /// <param name="session">Session to persist.</param>
  /// <param name="cancellationToken">Token that cancels the write.</param>
  /// <returns>Mutation status.</returns>
  Task<SupportMutationStatus> CreateSessionAsync(
      SupportDiagnosticSession session,
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
