using System.Globalization;
using System.Text;
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
  private const int MaxActivityBatchSize = 256;

  public async Task<SupportMutationStatus> CreateEnrollmentAsync(
      SupportEnrollment enrollment,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO support_enrollments (
            enrollment_id,
            tenant_id,
            display_name,
            enrollment_code_hash,
            created_by_github_user_id,
            created_at,
            expires_at)
        VALUES (
            $enrollmentId,
            $tenantId,
            $displayName,
            $enrollmentCodeHash,
            $createdByGitHubUserId,
            $createdAt,
            $expiresAt);
        """;
    AddEnrollmentParameters(command, enrollment);
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

  public async Task<SupportEnrollment?> GetEnrollmentOrNullAsync(
      string tenantId,
      string enrollmentCodeHash,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            enrollment_id,
            tenant_id,
            display_name,
            enrollment_code_hash,
            created_by_github_user_id,
            created_at,
            expires_at,
            consumed_at,
            recovery_expires_at,
            completion_id,
            completed_node_id,
            transport_credential_envelope_json
        FROM support_enrollments
        WHERE tenant_id = $tenantId
          AND enrollment_code_hash = $enrollmentCodeHash;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$enrollmentCodeHash", enrollmentCodeHash);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken) ? ReadEnrollment(reader) : null;
  }

  public async Task PurgeExpiredEnrollmentsAsync(
      DateTimeOffset now,
      int limit,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        DELETE FROM support_enrollments
        WHERE rowid IN (
            SELECT rowid
            FROM support_enrollments
            WHERE (consumed_at IS NULL AND expires_at < $now)
               OR (consumed_at IS NOT NULL AND recovery_expires_at < $now)
            ORDER BY COALESCE(recovery_expires_at, expires_at)
            LIMIT $limit);
        """;
    command.Parameters.AddWithValue("$now", Format(now));
    command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 256));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  public async Task<SupportMutationStatus> CompleteEnrollmentAsync(
      Guid enrollmentId,
      Guid completionId,
      SupportIdentityWrite write,
      SupportEnvelope transportCredentialEnvelope,
      DateTimeOffset consumedAt,
      DateTimeOffset recoveryExpiresAt,
      Guid relayCleanupLeaseId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(
        cancellationToken);
    await using var insert = connection.CreateCommand();
    insert.Transaction = transaction;
    insert.CommandText =
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
            enrollment_consumed_at,
            created_by_github_user_id,
            created_at,
            capability_version)
        SELECT
            $nodeId,
            $tenantId,
            $displayName,
            $nodeSigningPublicKeySpki,
            $nodeEncryptionPublicKeySpki,
            $transportCredentialHash,
            $enrollmentCodeHash,
            $enrollmentExpiresAt,
            $consumedAt,
            $createdByGitHubUserId,
            $createdAt,
            $capabilityVersion
        FROM support_enrollments
        WHERE enrollment_id = $enrollmentId
          AND tenant_id = $tenantId
          AND enrollment_code_hash = $enrollmentCodeHash
          AND consumed_at IS NULL
          AND expires_at >= $consumedAt
          AND NOT EXISTS (
              SELECT 1
              FROM support_nodes
              WHERE node_signing_public_key_spki =
                        $nodeSigningPublicKeySpki
                AND node_encryption_public_key_spki =
                        $nodeEncryptionPublicKeySpki);
        """;
    AddIdentityParameters(insert, write);
    insert.Parameters.AddWithValue("$enrollmentId", enrollmentId.ToString("D"));
    insert.Parameters.AddWithValue("$consumedAt", Format(consumedAt));
    try
    {
      if (await insert.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        await using var duplicate = connection.CreateCommand();
        duplicate.Transaction = transaction;
        duplicate.CommandText =
            """
            SELECT 1
            FROM support_nodes
            WHERE node_signing_public_key_spki =
                      $nodeSigningPublicKeySpki
              AND node_encryption_public_key_spki =
                      $nodeEncryptionPublicKeySpki;
            """;
        duplicate.Parameters.AddWithValue(
            "$nodeSigningPublicKeySpki",
            write.Identity.NodeSigningPublicKeySpki);
        duplicate.Parameters.AddWithValue(
            "$nodeEncryptionPublicKeySpki",
            write.Identity.NodeEncryptionPublicKeySpki);
        var duplicateExists =
            await duplicate.ExecuteScalarAsync(cancellationToken) is not null;
        await transaction.RollbackAsync(cancellationToken);
        return duplicateExists
            ? SupportMutationStatus.Conflict
            : SupportMutationStatus.Invalid;
      }
      await using var consume = connection.CreateCommand();
      consume.Transaction = transaction;
      consume.CommandText =
          """
          UPDATE support_enrollments
          SET consumed_at = $consumedAt,
              recovery_expires_at = $recoveryExpiresAt,
              completion_id = $completionId,
              completed_node_id = $nodeId,
              transport_credential_envelope_json =
                  $transportCredentialEnvelopeJson
          WHERE enrollment_id = $enrollmentId
            AND consumed_at IS NULL;
          """;
      consume.Parameters.AddWithValue("$enrollmentId", enrollmentId.ToString("D"));
      consume.Parameters.AddWithValue("$consumedAt", Format(consumedAt));
      consume.Parameters.AddWithValue(
          "$recoveryExpiresAt",
          Format(recoveryExpiresAt));
      consume.Parameters.AddWithValue("$completionId", completionId.ToString("D"));
      consume.Parameters.AddWithValue("$nodeId", write.Identity.NodeId.ToString("D"));
      consume.Parameters.AddWithValue(
          "$transportCredentialEnvelopeJson",
          JsonSerializer.Serialize(
              transportCredentialEnvelope,
              SupportJsonOptions));
      if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        await transaction.RollbackAsync(cancellationToken);
        return SupportMutationStatus.Conflict;
      }
      await using var completeCleanup = connection.CreateCommand();
      completeCleanup.Transaction = transaction;
      completeCleanup.CommandText =
          """
          DELETE FROM support_relay_cleanup
          WHERE node_id = $nodeId
            AND lease_id = $relayCleanupLeaseId;
          """;
      completeCleanup.Parameters.AddWithValue(
          "$nodeId",
          write.Identity.NodeId.ToString("D"));
      completeCleanup.Parameters.AddWithValue(
          "$relayCleanupLeaseId",
          relayCleanupLeaseId.ToString("D"));
      if (await completeCleanup.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        await transaction.RollbackAsync(cancellationToken);
        return SupportMutationStatus.Conflict;
      }
      await transaction.CommitAsync(cancellationToken);
      return SupportMutationStatus.Succeeded;
    }
    catch (SqliteException)
    {
      await transaction.RollbackAsync(cancellationToken);
      return SupportMutationStatus.Conflict;
    }
  }

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
        SELECT
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
            $capabilityVersion
        WHERE NOT EXISTS (
            SELECT 1
            FROM support_nodes
            WHERE node_signing_public_key_spki =
                      $nodeSigningPublicKeySpki
              AND node_encryption_public_key_spki =
                      $nodeEncryptionPublicKeySpki);
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

  public async Task QueueRelayCleanupAsync(
      Guid nodeId,
      DateTimeOffset createdAt,
      Guid leaseId,
      DateTimeOffset leaseExpiresAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO support_relay_cleanup (
            node_id,
            created_at,
            next_attempt_at,
            lease_id,
            lease_expires_at)
        VALUES (
            $nodeId,
            $createdAt,
            $createdAt,
            $leaseId,
            $leaseExpiresAt)
        ON CONFLICT (node_id) DO NOTHING;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$createdAt", Format(createdAt));
    command.Parameters.AddWithValue("$leaseId", leaseId.ToString("D"));
    command.Parameters.AddWithValue(
        "$leaseExpiresAt",
        Format(leaseExpiresAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  public async Task<IReadOnlyList<SupportRelayCleanup>> ClaimRelayCleanupAsync(
      DateTimeOffset now,
      Guid leaseId,
      DateTimeOffset leaseExpiresAt,
      int limit,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        WITH candidates AS (
            SELECT node_id
            FROM support_relay_cleanup
            WHERE next_attempt_at <= $now
              AND (
                  lease_id IS NULL
                  OR lease_expires_at <= $now)
            ORDER BY next_attempt_at, created_at, node_id
            LIMIT $limit
        )
        UPDATE support_relay_cleanup
        SET last_attempt_at = $now,
            attempt_count = attempt_count + 1,
            lease_id = $leaseId,
            lease_expires_at = $leaseExpiresAt
        WHERE node_id IN (SELECT node_id FROM candidates)
          AND next_attempt_at <= $now
          AND (
              lease_id IS NULL
              OR lease_expires_at <= $now)
        RETURNING
            node_id,
            created_at,
            last_attempt_at,
            attempt_count,
            next_attempt_at,
            lease_id,
            lease_expires_at;
        """;
    command.Parameters.AddWithValue("$now", Format(now));
    command.Parameters.AddWithValue("$leaseId", leaseId.ToString("D"));
    command.Parameters.AddWithValue(
        "$leaseExpiresAt",
        Format(leaseExpiresAt));
    command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 32));
    var cleanup = new List<SupportRelayCleanup>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      cleanup.Add(new SupportRelayCleanup(
          Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
          ParseDate(reader.GetString(1)),
          ReadNullableDate(reader, 2),
          reader.GetInt32(3),
          ParseDate(reader.GetString(4)),
          Guid.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
          ParseDate(reader.GetString(6))));
    }
    return cleanup
        .OrderBy(static item => item.NextAttemptAt)
        .ThenBy(static item => item.CreatedAt)
        .ThenBy(static item => item.NodeId)
        .ToArray();
  }

  public async Task<bool> RecordRelayCleanupAttemptAsync(
      Guid nodeId,
      Guid leaseId,
      DateTimeOffset attemptedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE support_relay_cleanup
        SET last_attempt_at = $attemptedAt,
            attempt_count = attempt_count + 1
        WHERE node_id = $nodeId
          AND lease_id = $leaseId;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$leaseId", leaseId.ToString("D"));
    command.Parameters.AddWithValue("$attemptedAt", Format(attemptedAt));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
  }

  public async Task<bool> DeferRelayCleanupAsync(
      Guid nodeId,
      Guid leaseId,
      DateTimeOffset nextAttemptAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE support_relay_cleanup
        SET next_attempt_at = $nextAttemptAt,
            lease_id = NULL,
            lease_expires_at = NULL
        WHERE node_id = $nodeId
          AND lease_id = $leaseId;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$leaseId", leaseId.ToString("D"));
    command.Parameters.AddWithValue(
        "$nextAttemptAt",
        Format(nextAttemptAt));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
  }

  public async Task<bool> CompleteRelayCleanupAsync(
      Guid nodeId,
      Guid leaseId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        DELETE FROM support_relay_cleanup
        WHERE node_id = $nodeId
          AND lease_id = $leaseId;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$leaseId", leaseId.ToString("D"));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
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

  public async Task UpdateIdentityActivityAsync(
      string tenantId,
      IReadOnlyList<SupportIdentityActivity> activity,
      CancellationToken cancellationToken)
  {
    if (activity.Count == 0)
    {
      return;
    }
    ArgumentOutOfRangeException.ThrowIfGreaterThan(
        activity.Count,
        MaxActivityBatchSize,
        nameof(activity));
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    var values = new StringBuilder(activity.Count * 64);
    for (var index = 0; index < activity.Count; index++)
    {
      if (index > 0)
      {
        values.Append(", ");
      }
      var suffix = index.ToString(CultureInfo.InvariantCulture);
      var nodeParameterName = $"$nodeId{suffix}";
      var pollParameterName = $"$lastPollAt{suffix}";
      var resultParameterName = $"$lastResultAt{suffix}";
      values
          .Append('(')
          .Append(nodeParameterName)
          .Append(", ")
          .Append(pollParameterName)
          .Append(", ")
          .Append(resultParameterName)
          .Append(')');
      var item = activity[index];
      command.Parameters
          .Add(nodeParameterName, SqliteType.Text)
          .Value = item.NodeId.ToString("D", CultureInfo.InvariantCulture);
      command.Parameters
          .Add(pollParameterName, SqliteType.Text)
          .Value = item.LastPollAt is null
              ? DBNull.Value
              : Format(item.LastPollAt.Value.ToUniversalTime());
      command.Parameters
          .Add(resultParameterName, SqliteType.Text)
          .Value = item.LastResultAt is null
              ? DBNull.Value
              : Format(item.LastResultAt.Value.ToUniversalTime());
    }
    command.CommandText =
        $"""
        WITH activity(node_id, last_poll_at, last_result_at) AS (
            VALUES {values}
        )
        UPDATE support_nodes AS target
        SET last_poll_at = CASE
                WHEN activity.last_poll_at IS NOT NULL
                  AND (target.last_poll_at IS NULL
                    OR target.last_poll_at < activity.last_poll_at)
                THEN activity.last_poll_at
                ELSE target.last_poll_at
            END,
            last_result_at = CASE
                WHEN activity.last_result_at IS NOT NULL
                  AND (target.last_result_at IS NULL
                    OR target.last_result_at < activity.last_result_at)
                THEN activity.last_result_at
                ELSE target.last_result_at
            END
        FROM activity
        WHERE target.tenant_id = $tenantId
          AND target.node_id = activity.node_id;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    await command.ExecuteNonQueryAsync(cancellationToken);
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

  public async Task<SupportIdentityRotationStatus> GetIdentityRotationStatusAsync(
      SupportIdentityRotation rotation,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await GetIdentityRotationStatusAsync(
        connection,
        transaction: null,
        rotation,
        cancellationToken);
  }

  public async Task<StoredSupportIdentityRotation?>
      GetIdentityRotationOrNullAsync(
          string tenantId,
          Guid nodeId,
          Guid rotationId,
          CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            expected_transport_credential_hash,
            replacement_transport_credential_hash,
            node_signing_public_key_spki,
            node_encryption_public_key_spki,
            phase,
            created_at,
            dashboard_promoted_at,
            finalized_at
        FROM support_identity_rotations
        WHERE rotation_id = $rotationId
          AND tenant_id = $tenantId
          AND node_id = $nodeId;
        """;
    command.Parameters.AddWithValue("$rotationId", rotationId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return null;
    }
    var phase = reader.GetString(4) switch
    {
      "prepared" => SupportIdentityRotationPhase.Prepared,
      "dashboard_promoted" =>
          SupportIdentityRotationPhase.DashboardPromoted,
      "finalized" => SupportIdentityRotationPhase.Finalized,
      _ => throw new InvalidOperationException(
          "Stored support identity rotation phase is invalid."),
    };
    return new StoredSupportIdentityRotation(
        new SupportIdentityRotation(
            rotationId,
            tenantId,
            nodeId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3)),
        phase,
        ParseDate(reader.GetString(5)),
        ReadNullableDate(reader, 6),
        ReadNullableDate(reader, 7));
  }

  public async Task<SupportMutationStatus> PrepareIdentityRotationAsync(
      SupportIdentityRotation rotation,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var status = await GetIdentityRotationStatusAsync(
        connection,
        transaction,
        rotation,
        cancellationToken);
    if (status is SupportIdentityRotationStatus.Prepared or
        SupportIdentityRotationStatus.DashboardPromoted or
        SupportIdentityRotationStatus.Finalized)
    {
      await transaction.CommitAsync(cancellationToken);
      return SupportMutationStatus.Succeeded;
    }
    if (status != SupportIdentityRotationStatus.Authorized)
    {
      await transaction.RollbackAsync(cancellationToken);
      return MapRotationMutationStatus(status);
    }
    await using (var removeFinalized = connection.CreateCommand())
    {
      removeFinalized.Transaction = transaction;
      removeFinalized.CommandText =
          """
          DELETE FROM support_identity_rotations
          WHERE node_id = $nodeId
            AND phase = 'finalized';
          """;
      removeFinalized.Parameters.AddWithValue(
          "$nodeId",
          rotation.NodeId.ToString("D"));
      await removeFinalized.ExecuteNonQueryAsync(cancellationToken);
    }
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO support_identity_rotations (
            rotation_id,
            tenant_id,
            node_id,
            expected_transport_credential_hash,
            replacement_transport_credential_hash,
            node_signing_public_key_spki,
            node_encryption_public_key_spki,
            phase,
            created_at)
        VALUES (
            $rotationId,
            $tenantId,
            $nodeId,
            $expectedTransportCredentialHash,
            $replacementTransportCredentialHash,
            $nodeSigningPublicKeySpki,
            $nodeEncryptionPublicKeySpki,
            'prepared',
            $createdAt);
        """;
    AddRotationParameters(command, rotation);
    command.Parameters.AddWithValue(
        "$rotationId",
        rotation.RotationId.ToString("D"));
    command.Parameters.AddWithValue("$createdAt", Format(createdAt));
    try
    {
      await command.ExecuteNonQueryAsync(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
      return SupportMutationStatus.Succeeded;
    }
    catch (SqliteException)
    {
      await transaction.RollbackAsync(cancellationToken);
      return SupportMutationStatus.Conflict;
    }
  }

  public async Task<SupportMutationStatus> PromoteIdentityRotationAsync(
      SupportIdentityRotation rotation,
      DateTimeOffset promotedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var status = await GetIdentityRotationStatusAsync(
        connection,
        transaction,
        rotation,
        cancellationToken);
    if (status is SupportIdentityRotationStatus.DashboardPromoted or
        SupportIdentityRotationStatus.Finalized or
        SupportIdentityRotationStatus.AlreadyApplied)
    {
      await transaction.CommitAsync(cancellationToken);
      return SupportMutationStatus.Succeeded;
    }
    if (status != SupportIdentityRotationStatus.Prepared)
    {
      await transaction.RollbackAsync(cancellationToken);
      return MapRotationMutationStatus(status);
    }
    await using (var promoteNode = connection.CreateCommand())
    {
      promoteNode.Transaction = transaction;
      promoteNode.CommandText =
          """
          UPDATE support_nodes
          SET node_signing_public_key_spki = $nodeSigningPublicKeySpki,
              node_encryption_public_key_spki = $nodeEncryptionPublicKeySpki,
              transport_credential_hash = $replacementTransportCredentialHash
          WHERE tenant_id = $tenantId
            AND node_id = $nodeId
            AND revoked_at IS NULL
            AND transport_credential_hash = $expectedTransportCredentialHash
            AND NOT EXISTS (
                SELECT 1
                FROM support_sessions
                WHERE tenant_id = $tenantId
                  AND node_id = $nodeId
                  AND status IN ('queued', 'dispatched'))
            AND NOT EXISTS (
                SELECT 1
                FROM support_nodes AS duplicate
                WHERE duplicate.node_id <> $nodeId
                  AND duplicate.node_signing_public_key_spki =
                          $nodeSigningPublicKeySpki
                  AND duplicate.node_encryption_public_key_spki =
                          $nodeEncryptionPublicKeySpki);
          """;
      AddRotationParameters(promoteNode, rotation);
      if (await promoteNode.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        await transaction.RollbackAsync(cancellationToken);
        return SupportMutationStatus.Conflict;
      }
    }
    await using var promoteRotation = connection.CreateCommand();
    promoteRotation.Transaction = transaction;
    promoteRotation.CommandText =
        """
        UPDATE support_identity_rotations
        SET phase = 'dashboard_promoted',
            dashboard_promoted_at = $promotedAt
        WHERE rotation_id = $rotationId
          AND tenant_id = $tenantId
          AND node_id = $nodeId
          AND phase = 'prepared';
        """;
    promoteRotation.Parameters.AddWithValue(
        "$rotationId",
        rotation.RotationId.ToString("D"));
    promoteRotation.Parameters.AddWithValue("$tenantId", rotation.TenantId);
    promoteRotation.Parameters.AddWithValue(
        "$nodeId",
        rotation.NodeId.ToString("D"));
    promoteRotation.Parameters.AddWithValue("$promotedAt", Format(promotedAt));
    if (await promoteRotation.ExecuteNonQueryAsync(cancellationToken) != 1)
    {
      await transaction.RollbackAsync(cancellationToken);
      return SupportMutationStatus.Conflict;
    }
    await transaction.CommitAsync(cancellationToken);
    return SupportMutationStatus.Succeeded;
  }

  public async Task<SupportMutationStatus> FinalizeIdentityRotationAsync(
      SupportIdentityRotation rotation,
      DateTimeOffset finalizedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE support_identity_rotations
        SET phase = 'finalized',
            finalized_at = COALESCE(finalized_at, $finalizedAt)
        WHERE rotation_id = $rotationId
          AND tenant_id = $tenantId
          AND node_id = $nodeId
          AND expected_transport_credential_hash =
                  $expectedTransportCredentialHash
          AND replacement_transport_credential_hash =
                  $replacementTransportCredentialHash
          AND node_signing_public_key_spki = $nodeSigningPublicKeySpki
          AND node_encryption_public_key_spki = $nodeEncryptionPublicKeySpki
          AND phase IN ('dashboard_promoted', 'finalized');
        """;
    AddRotationParameters(command, rotation);
    command.Parameters.AddWithValue(
        "$rotationId",
        rotation.RotationId.ToString("D"));
    command.Parameters.AddWithValue("$finalizedAt", Format(finalizedAt));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1
        ? SupportMutationStatus.Succeeded
        : SupportMutationStatus.Conflict;
  }

  public async Task<SupportMutationStatus> CreateSessionAsync(
      SupportDiagnosticSession session,
      string expectedNodeSigningPublicKeySpki,
      string expectedNodeEncryptionPublicKeySpki,
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
              AND revoked_at IS NULL
              AND node_signing_public_key_spki =
                  $expectedNodeSigningPublicKeySpki
              AND node_encryption_public_key_spki =
                  $expectedNodeEncryptionPublicKeySpki
              AND NOT EXISTS (
                  SELECT 1
                  FROM support_identity_rotations
                  WHERE tenant_id = $tenantId
                    AND node_id = $nodeId
                    AND phase IN ('prepared', 'dashboard_promoted')));
        """;
    AddSessionInsertParameters(command, session);
    command.Parameters.AddWithValue(
        "$expectedNodeSigningPublicKeySpki",
        expectedNodeSigningPublicKeySpki);
    command.Parameters.AddWithValue(
        "$expectedNodeEncryptionPublicKeySpki",
        expectedNodeEncryptionPublicKeySpki);
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

  public async Task<SupportMutationStatus>
      UpdateSessionLifecycleAsync(
          string tenantId,
          Guid sessionId,
          SupportDiagnosticSessionStatus status,
          DateTimeOffset? dispatchedAt,
          string? rejectionDisposition,
          DateTimeOffset transitionedAt,
          CancellationToken cancellationToken)
  {
    if (status is not (
            SupportDiagnosticSessionStatus.Dispatched or
            SupportDiagnosticSessionStatus.Rejected or
            SupportDiagnosticSessionStatus.Cancelled or
            SupportDiagnosticSessionStatus.Expired) ||
        (status == SupportDiagnosticSessionStatus.Rejected) !=
            (rejectionDisposition is not null) ||
        rejectionDisposition is not null &&
        !SupportRequestRejectionDispositions.IsSupported(
            rejectionDisposition))
    {
      return SupportMutationStatus.Invalid;
    }
    await using var connection =
        await _connectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE support_sessions
        SET status = $status,
            dispatched_at = CASE
                WHEN dispatched_at IS NULL THEN $dispatchedAt
                WHEN $dispatchedAt IS NULL THEN dispatched_at
                WHEN dispatched_at <= $dispatchedAt THEN dispatched_at
                ELSE $dispatchedAt
            END,
            rejection_disposition = $rejectionDisposition,
            completed_at = CASE
                WHEN $status = 'dispatched' THEN completed_at
                ELSE COALESCE(completed_at, $transitionedAt)
            END,
            cancelled_at = CASE
                WHEN $status = 'cancelled'
                THEN COALESCE(cancelled_at, $transitionedAt)
                ELSE cancelled_at
            END
        WHERE tenant_id = $tenantId
          AND session_id = $sessionId
          AND (
              status IN ('queued', 'dispatched')
              OR (
                  status = $status
                  AND (
                      rejection_disposition IS $rejectionDisposition
                      OR rejection_disposition = $rejectionDisposition)));
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$sessionId",
        sessionId.ToString("D"));
    command.Parameters.AddWithValue(
        "$status",
        status.ToString().ToLowerInvariant());
    command.Parameters.AddWithValue(
        "$dispatchedAt",
        dispatchedAt is null
            ? DBNull.Value
            : Format(dispatchedAt.Value));
    command.Parameters.AddWithValue(
        "$rejectionDisposition",
        rejectionDisposition is null
            ? DBNull.Value
            : rejectionDisposition);
    command.Parameters.AddWithValue(
        "$transitionedAt",
        Format(transitionedAt));
    return await command.ExecuteNonQueryAsync(
        cancellationToken) == 1
        ? SupportMutationStatus.Succeeded
        : SupportMutationStatus.Conflict;
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
          s.dispatched_at,
          s.rejection_disposition,
          s.completed_at,
          s.result_envelope_json,
          s.report_json,
          s.markdown,
          s.attestation_json
      FROM support_sessions AS s
      """;

  private static async Task<SupportIdentityRotationStatus>
      GetIdentityRotationStatusAsync(
          SqliteConnection connection,
          SqliteTransaction? transaction,
          SupportIdentityRotation rotation,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            node.transport_credential_hash,
            node.node_signing_public_key_spki,
            node.node_encryption_public_key_spki,
            node.revoked_at,
            EXISTS (
                SELECT 1
                FROM support_sessions
                WHERE tenant_id = $tenantId
                  AND node_id = $nodeId
                  AND status IN ('queued', 'dispatched')),
            pending.rotation_id,
            pending.expected_transport_credential_hash,
            pending.replacement_transport_credential_hash,
            pending.node_signing_public_key_spki,
            pending.node_encryption_public_key_spki,
            pending.phase,
            EXISTS (
                SELECT 1
                FROM support_nodes AS duplicate
                WHERE duplicate.node_id <> $nodeId
                  AND (
                      duplicate.transport_credential_hash =
                          $replacementTransportCredentialHash
                      OR (
                          duplicate.node_signing_public_key_spki =
                              $nodeSigningPublicKeySpki
                          AND duplicate.node_encryption_public_key_spki =
                              $nodeEncryptionPublicKeySpki)))
            OR EXISTS (
                SELECT 1
                FROM support_identity_rotations AS duplicate_rotation
                WHERE duplicate_rotation.node_id <> $nodeId
                  AND duplicate_rotation.replacement_transport_credential_hash =
                          $replacementTransportCredentialHash)
        FROM support_nodes AS node
        LEFT JOIN support_identity_rotations AS pending
          ON pending.node_id = node.node_id
        WHERE node.tenant_id = $tenantId
          AND node.node_id = $nodeId;
        """;
    AddRotationParameters(command, rotation);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return SupportIdentityRotationStatus.NotFound;
    }
    if (!await reader.IsDBNullAsync(3, cancellationToken))
    {
      return SupportIdentityRotationStatus.Revoked;
    }
    var currentHash = reader.GetString(0);
    var currentSigningKey = reader.GetString(1);
    var currentEncryptionKey = reader.GetString(2);
    if (!await reader.IsDBNullAsync(5, cancellationToken))
    {
      var exactRotation =
          Guid.Parse(
              reader.GetString(5),
              CultureInfo.InvariantCulture) == rotation.RotationId &&
          string.Equals(
              reader.GetString(6),
              rotation.ExpectedTransportCredentialHash,
              StringComparison.Ordinal) &&
          string.Equals(
              reader.GetString(7),
              rotation.ReplacementTransportCredentialHash,
              StringComparison.Ordinal) &&
          string.Equals(
              reader.GetString(8),
              rotation.NodeSigningPublicKeySpki,
              StringComparison.Ordinal) &&
          string.Equals(
              reader.GetString(9),
              rotation.NodeEncryptionPublicKeySpki,
              StringComparison.Ordinal);
      var phase = reader.GetString(10);
      if (!exactRotation &&
          !string.Equals(phase, "finalized", StringComparison.Ordinal))
      {
        return SupportIdentityRotationStatus.Conflict;
      }
      if (exactRotation)
      {
        return phase switch
        {
          "prepared" => SupportIdentityRotationStatus.Prepared,
          "dashboard_promoted" =>
              SupportIdentityRotationStatus.DashboardPromoted,
          "finalized" => SupportIdentityRotationStatus.Finalized,
          _ => SupportIdentityRotationStatus.Conflict,
        };
      }
    }
    var alreadyApplied =
        string.Equals(
            currentHash,
            rotation.ReplacementTransportCredentialHash,
            StringComparison.Ordinal) &&
        string.Equals(
            currentSigningKey,
            rotation.NodeSigningPublicKeySpki,
            StringComparison.Ordinal) &&
        string.Equals(
            currentEncryptionKey,
            rotation.NodeEncryptionPublicKeySpki,
            StringComparison.Ordinal);
    if (alreadyApplied)
    {
      return SupportIdentityRotationStatus.AlreadyApplied;
    }
    if (!string.Equals(
        currentHash,
        rotation.ExpectedTransportCredentialHash,
        StringComparison.Ordinal))
    {
      return SupportIdentityRotationStatus.Forbidden;
    }
    if (reader.GetBoolean(4))
    {
      return SupportIdentityRotationStatus.ActiveSessions;
    }
    return reader.GetBoolean(11)
        ? SupportIdentityRotationStatus.Conflict
        : SupportIdentityRotationStatus.Authorized;
  }

  private static SupportMutationStatus MapRotationMutationStatus(
      SupportIdentityRotationStatus status) =>
      status switch
      {
        SupportIdentityRotationStatus.NotFound =>
            SupportMutationStatus.NotFound,
        SupportIdentityRotationStatus.Revoked =>
            SupportMutationStatus.Revoked,
        SupportIdentityRotationStatus.Forbidden =>
            SupportMutationStatus.Forbidden,
        _ => SupportMutationStatus.Conflict,
      };

  private static void AddEnrollmentParameters(
      SqliteCommand command,
      SupportEnrollment enrollment)
  {
    command.Parameters.AddWithValue(
        "$enrollmentId",
        enrollment.EnrollmentId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", enrollment.TenantId);
    command.Parameters.AddWithValue("$displayName", enrollment.DisplayName);
    command.Parameters.AddWithValue(
        "$enrollmentCodeHash",
        enrollment.EnrollmentCodeHash);
    command.Parameters.AddWithValue(
        "$createdByGitHubUserId",
        enrollment.CreatedByGitHubUserId);
    command.Parameters.AddWithValue("$createdAt", Format(enrollment.CreatedAt));
    command.Parameters.AddWithValue("$expiresAt", Format(enrollment.ExpiresAt));
  }

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

  private static void AddRotationParameters(
      SqliteCommand command,
      SupportIdentityRotation rotation)
  {
    command.Parameters.AddWithValue("$tenantId", rotation.TenantId);
    command.Parameters.AddWithValue("$nodeId", rotation.NodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$expectedTransportCredentialHash",
        rotation.ExpectedTransportCredentialHash);
    command.Parameters.AddWithValue(
        "$replacementTransportCredentialHash",
        rotation.ReplacementTransportCredentialHash);
    command.Parameters.AddWithValue(
        "$nodeSigningPublicKeySpki",
        rotation.NodeSigningPublicKeySpki);
    command.Parameters.AddWithValue(
        "$nodeEncryptionPublicKeySpki",
        rotation.NodeEncryptionPublicKeySpki);
  }

  private static SupportEnrollment ReadEnrollment(SqliteDataReader reader) =>
      new(
          Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
          reader.GetString(1),
          reader.GetString(2),
          reader.GetString(3),
          reader.GetString(4),
          ParseDate(reader.GetString(5)),
          ParseDate(reader.GetString(6)),
          ReadNullableDate(reader, 7),
          ReadNullableDate(reader, 8),
          reader.IsDBNull(9)
              ? null
              : Guid.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
          reader.IsDBNull(10)
              ? null
              : Guid.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
          reader.IsDBNull(11)
              ? null
              : JsonSerializer.Deserialize<SupportEnvelope>(
                  reader.GetString(11),
                  SupportJsonOptions));

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
    var report = reader.IsDBNull(18)
        ? (JsonElement?)null
        : JsonDocument.Parse(reader.GetString(18)).RootElement.Clone();
    var attestation = reader.IsDBNull(20)
        ? null
        : JsonSerializer.Deserialize<SupportResultAttestation>(reader.GetString(20), SupportJsonOptions);
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
        reader.IsDBNull(15) ? null : reader.GetString(15),
        ReadNullableDate(reader, 16),
        reader.IsDBNull(17)
            ? null
            : JsonSerializer.Deserialize<SupportEnvelope>(reader.GetString(17), SupportJsonOptions),
        report,
        reader.IsDBNull(19) ? null : reader.GetString(19),
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
