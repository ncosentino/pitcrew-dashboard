using System.Globalization;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed class SqliteSupportStore(
    SqliteConnectionFactory _connectionFactory) : ISupportStore
{
  public async Task<SupportMutationStatus> CreateIdentityAsync(
      SupportIdentityWrite write,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO support_nodes (
            node_id,
            tenant_id,
            display_name,
            node_signing_public_key_spki,
            node_encryption_public_key_spki,
            transport_credential_hash,
            enrollment_code_hash,
            enrollment_expires_at,
            created_by_github_user_id,
            created_at,
            capability_version)
        VALUES (
            $nodeId,
            $tenantId,
            $displayName,
            $nodeSigningPublicKeySpki,
            $nodeEncryptionPublicKeySpki,
            $transportCredentialHash,
            $enrollmentCodeHash,
            $enrollmentExpiresAt,
            $createdByGitHubUserId,
            $createdAt,
            $capabilityVersion);
        """;
    AddIdentityParameters(command, write);
    try
    {
      return await command.ExecuteNonQueryAsync(cancellationToken) == 1
          ? SupportMutationStatus.Succeeded
          : SupportMutationStatus.Conflict;
    }
    catch (SqliteException)
    {
      return SupportMutationStatus.Conflict;
    }
  }

  public async Task<IReadOnlyList<SupportIdentity>> GetIdentitiesAsync(
      string tenantId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            tenant_id,
            node_id,
            display_name,
            node_signing_public_key_spki,
            node_encryption_public_key_spki,
            created_by_github_user_id,
            created_at,
            revoked_at,
            revoked_by_github_user_id,
            last_poll_at,
            last_result_at,
            capability_version
        FROM support_nodes
        WHERE tenant_id = $tenantId
        ORDER BY created_at DESC, node_id DESC;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    var identities = new List<SupportIdentity>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      identities.Add(ReadIdentity(reader));
    }
    return identities;
  }

  public async Task<SupportIdentity?> GetIdentityOrNullAsync(
      string tenantId,
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            tenant_id,
            node_id,
            display_name,
            node_signing_public_key_spki,
            node_encryption_public_key_spki,
            created_by_github_user_id,
            created_at,
            revoked_at,
            revoked_by_github_user_id,
            last_poll_at,
            last_result_at,
            capability_version
        FROM support_nodes
        WHERE tenant_id = $tenantId
          AND node_id = $nodeId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken) ? ReadIdentity(reader) : null;
  }

  public async Task<SupportMutationStatus> RevokeIdentityAsync(
      string tenantId,
      Guid nodeId,
      string revokedByGitHubUserId,
      DateTimeOffset revokedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE support_nodes
        SET revoked_at = $revokedAt,
            revoked_by_github_user_id = $revokedByGitHubUserId
        WHERE tenant_id = $tenantId
          AND node_id = $nodeId
          AND revoked_at IS NULL;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$revokedByGitHubUserId", revokedByGitHubUserId);
    command.Parameters.AddWithValue("$revokedAt", Format(revokedAt));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1
        ? SupportMutationStatus.Succeeded
        : SupportMutationStatus.NotFound;
  }

  public async Task<SupportMutationStatus> CreateSessionAsync(
      SupportDiagnosticSession session,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO support_sessions (
            session_id,
            tenant_id,
            node_id,
            diagnostic_mode,
            profile_id,
            package_id,
            capability,
            request_digest,
            node_signing_key_fingerprint,
            status,
            requested_by_github_user_id,
            requested_at,
            expires_at,
            request_envelope_json)
        SELECT
            $sessionId,
            $tenantId,
            $nodeId,
            $diagnosticMode,
            $profileId,
            $packageId,
            $capability,
            $requestDigest,
            $nodeSigningKeyFingerprint,
            'queued',
            $requestedByGitHubUserId,
            $requestedAt,
            $expiresAt,
            $requestEnvelopeJson
        WHERE EXISTS (
            SELECT 1
            FROM support_nodes
            WHERE tenant_id = $tenantId
              AND node_id = $nodeId
              AND revoked_at IS NULL);
        """;
    AddSessionInsertParameters(command, session);
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1
        ? SupportMutationStatus.Succeeded
        : SupportMutationStatus.NotFound;
  }

  public async Task<SupportDiagnosticSession?> GetSessionOrNullAsync(
      string tenantId,
      Guid sessionId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = SelectSessionsSql +
        """

        WHERE s.tenant_id = $tenantId
          AND s.session_id = $sessionId
          AND s.capability = 'pitcrew.diagnostics.snapshot.v1'
          AND length(s.request_digest) = 64
          AND length(s.node_signing_key_fingerprint) = 64;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
  }

  public async Task<IReadOnlyList<SupportDiagnosticSession>> GetSessionsAsync(
      string tenantId,
      int limit,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = SelectSessionsSql +
        """

        WHERE s.tenant_id = $tenantId
          AND s.capability = 'pitcrew.diagnostics.snapshot.v1'
          AND length(s.request_digest) = 64
          AND length(s.node_signing_key_fingerprint) = 64
        ORDER BY s.requested_at DESC, s.session_id DESC
        LIMIT $limit;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
    var sessions = new List<SupportDiagnosticSession>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      sessions.Add(ReadSession(reader));
    }
    return sessions;
  }

  public async Task<SupportMutationStatus> CancelSessionAsync(
      string tenantId,
      Guid sessionId,
      DateTimeOffset cancelledAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE support_sessions
        SET status = 'cancelled',
            cancelled_at = $cancelledAt,
            completed_at = $cancelledAt
        WHERE tenant_id = $tenantId
          AND session_id = $sessionId
          AND status IN ('queued', 'dispatched');
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
    command.Parameters.AddWithValue("$cancelledAt", Format(cancelledAt));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1
        ? SupportMutationStatus.Succeeded
        : SupportMutationStatus.Conflict;
  }

  public async Task<SupportMutationStatus> CompleteSessionAsync(
      string tenantId,
      Guid sessionId,
      string result,
      string reportJson,
      string markdown,
      string attestationJson,
      DateTimeOffset completedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE support_sessions
        SET status = 'completed',
            completed_at = $completedAt,
            result_envelope_json = $result,
            report_json = $reportJson,
            markdown = $markdown,
            attestation_json = $attestationJson
        WHERE tenant_id = $tenantId
          AND session_id = $sessionId
          AND status IN ('queued', 'dispatched');
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
    command.Parameters.AddWithValue("$completedAt", Format(completedAt));
    command.Parameters.AddWithValue("$result", result);
    command.Parameters.AddWithValue("$reportJson", reportJson);
    command.Parameters.AddWithValue("$markdown", markdown);
    command.Parameters.AddWithValue("$attestationJson", attestationJson);
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1
        ? SupportMutationStatus.Succeeded
        : SupportMutationStatus.Conflict;
  }

  private const string SelectSessionsSql =
      """
      SELECT
          s.tenant_id,
          s.session_id,
          s.node_id,
          s.diagnostic_mode,
          s.profile_id,
          s.package_id,
          s.capability,
          s.request_digest,
          s.node_signing_key_fingerprint,
          s.status,
          s.requested_by_github_user_id,
          s.requested_at,
          s.expires_at,
          s.request_envelope_json,
          s.completed_at,
          s.result_envelope_json,
          s.report_json,
          s.markdown,
          s.attestation_json
      FROM support_sessions AS s
      """;

  private static void AddIdentityParameters(SqliteCommand command, SupportIdentityWrite write)
  {
    command.Parameters.AddWithValue("$nodeId", write.Identity.NodeId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", write.Identity.TenantId);
    command.Parameters.AddWithValue("$displayName", write.Identity.DisplayName);
    command.Parameters.AddWithValue("$nodeSigningPublicKeySpki", write.Identity.NodeSigningPublicKeySpki);
    command.Parameters.AddWithValue("$nodeEncryptionPublicKeySpki", write.Identity.NodeEncryptionPublicKeySpki);
    command.Parameters.AddWithValue("$transportCredentialHash", write.TransportCredentialHash);
    command.Parameters.AddWithValue("$enrollmentCodeHash", write.EnrollmentCodeHash);
    command.Parameters.AddWithValue("$enrollmentExpiresAt", Format(write.EnrollmentExpiresAt));
    command.Parameters.AddWithValue("$createdByGitHubUserId", write.Identity.CreatedByGitHubUserId);
    command.Parameters.AddWithValue("$createdAt", Format(write.Identity.CreatedAt));
    command.Parameters.AddWithValue("$capabilityVersion", write.Identity.CapabilityVersion);
  }

  private static void AddSessionInsertParameters(SqliteCommand command, SupportDiagnosticSession session)
  {
    command.Parameters.AddWithValue("$sessionId", session.SessionId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", session.TenantId);
    command.Parameters.AddWithValue("$nodeId", session.NodeId.ToString("D"));
    command.Parameters.AddWithValue("$diagnosticMode", session.DiagnosticMode);
    command.Parameters.AddWithValue("$profileId", (object?)session.ProfileId ?? DBNull.Value);
    command.Parameters.AddWithValue("$packageId", session.PackageId);
    command.Parameters.AddWithValue("$capability", session.Capability);
    command.Parameters.AddWithValue("$requestDigest", session.RequestDigest);
    command.Parameters.AddWithValue(
        "$nodeSigningKeyFingerprint",
        session.NodeSigningKeyFingerprint);
    command.Parameters.AddWithValue("$requestedByGitHubUserId", session.RequestedByGitHubUserId);
    command.Parameters.AddWithValue("$requestedAt", Format(session.RequestedAt));
    command.Parameters.AddWithValue("$expiresAt", Format(session.ExpiresAt));
    command.Parameters.AddWithValue("$requestEnvelopeJson", JsonSerializer.Serialize(session.RequestEnvelope, SupportJsonOptions));
  }

  private static SupportIdentity ReadIdentity(SqliteDataReader reader) =>
      new(
          reader.GetString(0),
          Guid.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
          reader.GetString(2),
          reader.GetString(3),
          reader.GetString(4),
          reader.GetString(5),
          ParseDate(reader.GetString(6)),
          ReadNullableDate(reader, 7),
          reader.IsDBNull(8) ? null : reader.GetString(8),
          ReadNullableDate(reader, 9),
          ReadNullableDate(reader, 10),
          reader.GetInt32(11));

  private static SupportDiagnosticSession ReadSession(SqliteDataReader reader)
  {
    var report = reader.IsDBNull(16)
        ? (JsonElement?)null
        : JsonDocument.Parse(reader.GetString(16)).RootElement.Clone();
    var attestation = reader.IsDBNull(18)
        ? null
        : JsonSerializer.Deserialize<SupportResultAttestation>(reader.GetString(18), SupportJsonOptions);
    return new SupportDiagnosticSession(
        reader.GetString(0),
        Guid.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
        Guid.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        ParseStatus(reader.GetString(9)),
        reader.GetString(10),
        ParseDate(reader.GetString(11)),
        ParseDate(reader.GetString(12)),
        JsonSerializer.Deserialize<SupportEnvelope>(reader.GetString(13), SupportJsonOptions) ??
            throw new InvalidOperationException("Stored support request envelope was invalid."),
        ReadNullableDate(reader, 14),
        reader.IsDBNull(15)
            ? null
            : JsonSerializer.Deserialize<SupportEnvelope>(reader.GetString(15), SupportJsonOptions),
        report,
        reader.IsDBNull(17) ? null : reader.GetString(17),
        attestation);
  }

  private static JsonSerializerOptions SupportJsonOptions { get; } = new(JsonSerializerDefaults.Web);

  private static string Format(DateTimeOffset value) =>
      value.ToString("O", CultureInfo.InvariantCulture);

  private static DateTimeOffset ParseDate(string value) =>
      DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

  private static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int ordinal) =>
      reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));

  private static SupportDiagnosticSessionStatus ParseStatus(string value) =>
      value switch
      {
        "queued" => SupportDiagnosticSessionStatus.Queued,
        "dispatched" => SupportDiagnosticSessionStatus.Dispatched,
        "completed" => SupportDiagnosticSessionStatus.Completed,
        "rejected" => SupportDiagnosticSessionStatus.Rejected,
        "cancelled" => SupportDiagnosticSessionStatus.Cancelled,
        "expired" => SupportDiagnosticSessionStatus.Expired,
        _ => throw new InvalidOperationException("Stored support session status was invalid."),
      };
}
