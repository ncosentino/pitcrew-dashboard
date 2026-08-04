namespace PitCrew.Dashboard.Features.Access.Abstractions;

/// <summary>
/// Persists and authenticates scoped, read-only diagnostic credentials.
/// </summary>
public interface IDiagnosticCredentialStore
{
  /// <summary>
  /// Creates one credential after validating its tenant and node restrictions.
  /// </summary>
  /// <param name="write">Credential metadata and one-way token hash.</param>
  /// <param name="cancellationToken">Token that cancels creation.</param>
  /// <returns>The mutation status.</returns>
  Task<DiagnosticCredentialMutationStatus> CreateAsync(
      DiagnosticCredentialWrite write,
      CancellationToken cancellationToken);

  /// <summary>
  /// Lists credential metadata for one tenant without returning token hashes.
  /// </summary>
  /// <param name="tenantId">Tenant whose credentials should be listed.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Credentials ordered by creation time descending.</returns>
  Task<IReadOnlyList<DiagnosticCredential>> GetAllAsync(
      string tenantId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Resolves and records successful use of one unexpired credential.
  /// </summary>
  /// <param name="credentialId">Credential identifier parsed from the raw token.</param>
  /// <param name="tokenHash">One-way hash of the presented raw token.</param>
  /// <param name="usedAt">Dashboard time of authentication.</param>
  /// <param name="cancellationToken">Token that cancels authentication.</param>
  /// <returns>The read scope, or <see langword="null"/> when authentication fails.</returns>
  Task<DiagnosticAccessScope?> ResolveOrNullAsync(
      Guid credentialId,
      string tokenHash,
      DateTimeOffset usedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Revokes one tenant credential.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the credential.</param>
  /// <param name="credentialId">Credential to revoke.</param>
  /// <param name="revokedByGitHubUserId">Administrator performing revocation.</param>
  /// <param name="revokedAt">Dashboard time of revocation.</param>
  /// <param name="cancellationToken">Token that cancels revocation.</param>
  /// <returns>The mutation status.</returns>
  Task<DiagnosticCredentialMutationStatus> RevokeAsync(
      string tenantId,
      Guid credentialId,
      string revokedByGitHubUserId,
      DateTimeOffset revokedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Atomically revokes one active credential and creates its replacement.
  /// </summary>
  /// <param name="tenantId">Tenant that owns both credentials.</param>
  /// <param name="credentialId">Credential being replaced.</param>
  /// <param name="replacementCredentialId">Dashboard-assigned replacement identifier.</param>
  /// <param name="replacementTokenHash">One-way hash of the replacement token.</param>
  /// <param name="rotatedByGitHubUserId">Administrator performing rotation.</param>
  /// <param name="rotatedAt">Dashboard time of rotation.</param>
  /// <param name="cancellationToken">Token that cancels rotation.</param>
  /// <returns>The mutation and replacement metadata when successful.</returns>
  Task<DiagnosticCredentialMutation> RotateAsync(
      string tenantId,
      Guid credentialId,
      Guid replacementCredentialId,
      string replacementTokenHash,
      string rotatedByGitHubUserId,
      DateTimeOffset rotatedAt,
      CancellationToken cancellationToken);
}
