using System.Globalization;

using Microsoft.Data.Sqlite;

namespace PitCrew.Support.Relay.App;

internal sealed class SqliteRelayStore(string _databasePath)
{
  public async Task InitializeAsync(CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(_databasePath) ?? ".");
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        CREATE TABLE IF NOT EXISTS relay_nodes (
            node_id TEXT PRIMARY KEY,
            tenant_id TEXT NOT NULL,
            transport_credential_hash TEXT NOT NULL UNIQUE,
            revoked_at TEXT NULL,
            last_poll_at TEXT NULL,
            last_result_at TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS relay_sessions (
            session_id TEXT PRIMARY KEY,
            tenant_id TEXT NOT NULL,
            node_id TEXT NOT NULL,
            status TEXT NOT NULL CHECK (status IN (
                'queued',
                'dispatched',
                'completed',
                'cancelled',
                'expired')),
            expires_at TEXT NOT NULL,
            request_envelope_json TEXT NOT NULL,
            result_envelope_json TEXT NULL,
            FOREIGN KEY (node_id) REFERENCES relay_nodes(node_id)
        );

        CREATE INDEX IF NOT EXISTS ix_relay_sessions_node_status_expiry
            ON relay_sessions (node_id, status, expires_at, session_id);

        CREATE TABLE IF NOT EXISTS relay_credential_rotations (
            rotation_id TEXT PRIMARY KEY,
            node_id TEXT NOT NULL UNIQUE,
            tenant_id TEXT NOT NULL,
            expected_transport_credential_hash TEXT NOT NULL,
            replacement_transport_credential_hash TEXT NOT NULL,
            phase TEXT NOT NULL CHECK (phase IN ('prepared', 'promoted')),
            created_at TEXT NOT NULL,
            promoted_at TEXT NULL,
            FOREIGN KEY (node_id)
                REFERENCES relay_nodes(node_id) ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS
            ux_relay_credential_rotations_replacement
            ON relay_credential_rotations (
                replacement_transport_credential_hash);
        """;
    await command.ExecuteNonQueryAsync(cancellationToken);
    await EnsureColumnAsync(
        connection,
        "relay_nodes",
        "last_poll_at",
        "TEXT NULL",
        cancellationToken);
    await EnsureColumnAsync(
        connection,
        "relay_nodes",
        "last_result_at",
        "TEXT NULL",
        cancellationToken);
  }

  public async Task<bool> RegisterNodeAsync(
      RelayNodeRegistrationRequest request,
      CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO relay_nodes (
            node_id,
            tenant_id,
            transport_credential_hash,
            revoked_at)
        VALUES ($nodeId, $tenantId, $hash, NULL)
        ON CONFLICT (node_id) DO UPDATE SET
            transport_credential_hash = excluded.transport_credential_hash,
            revoked_at = NULL
        WHERE relay_nodes.tenant_id = excluded.tenant_id;
        """;
    command.Parameters.AddWithValue("$nodeId", request.NodeId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", request.TenantId);
    command.Parameters.AddWithValue("$hash", request.TransportCredentialHash);
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
  }

  public async Task<bool> RevokeNodeAsync(
      Guid nodeId,
      DateTimeOffset revokedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE relay_nodes
        SET revoked_at = $revokedAt
        WHERE node_id = $nodeId;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$revokedAt", Format(revokedAt));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
  }

  public async Task<RelayCredentialRotationStatus> PrepareNodeCredentialAsync(
      Guid nodeId,
      RelayNodeCredentialRotationRequest request,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var status = await GetCredentialRotationStatusAsync(
        connection,
        transaction,
        nodeId,
        request,
        cancellationToken);
    if (status is RelayCredentialRotationStatus.Prepared or
        RelayCredentialRotationStatus.Promoted)
    {
      await transaction.CommitAsync(cancellationToken);
      return status;
    }
    if (status != RelayCredentialRotationStatus.Authorized)
    {
      await transaction.RollbackAsync(cancellationToken);
      return status;
    }
    await using (var removePromoted = connection.CreateCommand())
    {
      removePromoted.Transaction = transaction;
      removePromoted.CommandText =
          """
          DELETE FROM relay_credential_rotations
          WHERE node_id = $nodeId
            AND phase = 'promoted';
          """;
      removePromoted.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      await removePromoted.ExecuteNonQueryAsync(cancellationToken);
    }
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO relay_credential_rotations (
            rotation_id,
            node_id,
            tenant_id,
            expected_transport_credential_hash,
            replacement_transport_credential_hash,
            phase,
            created_at)
        VALUES (
            $rotationId,
            $nodeId,
            $tenantId,
            $expectedHash,
            $replacementHash,
            'prepared',
            $createdAt);
        """;
    command.Parameters.AddWithValue(
        "$rotationId",
        request.RotationId.ToString("D"));
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", request.TenantId);
    command.Parameters.AddWithValue(
        "$expectedHash",
        request.ExpectedTransportCredentialHash);
    command.Parameters.AddWithValue(
        "$replacementHash",
        request.ReplacementTransportCredentialHash);
    command.Parameters.AddWithValue("$createdAt", Format(createdAt));
    try
    {
      await command.ExecuteNonQueryAsync(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
      return RelayCredentialRotationStatus.Prepared;
    }
    catch (SqliteException)
    {
      await transaction.RollbackAsync(cancellationToken);
      return RelayCredentialRotationStatus.Conflict;
    }
  }

  public async Task<RelayCredentialRotationStatus> PromoteNodeCredentialAsync(
      Guid nodeId,
      RelayNodeCredentialRotationRequest request,
      DateTimeOffset promotedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var status = await GetCredentialRotationStatusAsync(
        connection,
        transaction,
        nodeId,
        request,
        cancellationToken);
    if (status == RelayCredentialRotationStatus.Promoted)
    {
      await transaction.CommitAsync(cancellationToken);
      return status;
    }
    if (status != RelayCredentialRotationStatus.Prepared)
    {
      await transaction.RollbackAsync(cancellationToken);
      return status;
    }
    await using (var promoteNode = connection.CreateCommand())
    {
      promoteNode.Transaction = transaction;
      promoteNode.CommandText =
          """
          UPDATE relay_nodes
          SET transport_credential_hash = $replacementHash
          WHERE node_id = $nodeId
            AND tenant_id = $tenantId
            AND revoked_at IS NULL
            AND transport_credential_hash = $expectedHash;
          """;
      promoteNode.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      promoteNode.Parameters.AddWithValue("$tenantId", request.TenantId);
      promoteNode.Parameters.AddWithValue(
          "$expectedHash",
          request.ExpectedTransportCredentialHash);
      promoteNode.Parameters.AddWithValue(
          "$replacementHash",
          request.ReplacementTransportCredentialHash);
      if (await promoteNode.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        await transaction.RollbackAsync(cancellationToken);
        return RelayCredentialRotationStatus.Conflict;
      }
    }
    await using var promoteRotation = connection.CreateCommand();
    promoteRotation.Transaction = transaction;
    promoteRotation.CommandText =
        """
        UPDATE relay_credential_rotations
        SET phase = 'promoted',
            promoted_at = $promotedAt
        WHERE rotation_id = $rotationId
          AND node_id = $nodeId
          AND phase = 'prepared';
        """;
    promoteRotation.Parameters.AddWithValue(
        "$rotationId",
        request.RotationId.ToString("D"));
    promoteRotation.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    promoteRotation.Parameters.AddWithValue("$promotedAt", Format(promotedAt));
    if (await promoteRotation.ExecuteNonQueryAsync(cancellationToken) != 1)
    {
      await transaction.RollbackAsync(cancellationToken);
      return RelayCredentialRotationStatus.Conflict;
    }
    await transaction.CommitAsync(cancellationToken);
    return RelayCredentialRotationStatus.Promoted;
  }

  public async Task<bool> EnqueueSessionAsync(
      RelaySessionEnqueueRequest request,
      CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO relay_sessions (
            session_id,
            tenant_id,
            node_id,
            status,
            expires_at,
            request_envelope_json)
        SELECT
            $sessionId,
            $tenantId,
            $nodeId,
            'queued',
            $expiresAt,
            $requestEnvelope
        FROM relay_nodes
        WHERE node_id = $nodeId
          AND tenant_id = $tenantId
          AND revoked_at IS NULL
        ON CONFLICT (session_id) DO NOTHING;
        """;
    command.Parameters.AddWithValue("$sessionId", request.SessionId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", request.TenantId);
    command.Parameters.AddWithValue("$nodeId", request.NodeId.ToString("D"));
    command.Parameters.AddWithValue("$expiresAt", Format(request.ExpiresAt));
    command.Parameters.AddWithValue("$requestEnvelope", request.RequestEnvelope);
    var inserted = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    if (inserted)
    {
      return true;
    }
    await using var existing = connection.CreateCommand();
    existing.CommandText =
        """
        SELECT 1
        FROM relay_sessions
        WHERE session_id = $sessionId
          AND tenant_id = $tenantId
          AND node_id = $nodeId;
        """;
    existing.Parameters.AddWithValue("$sessionId", request.SessionId.ToString("D"));
    existing.Parameters.AddWithValue("$tenantId", request.TenantId);
    existing.Parameters.AddWithValue("$nodeId", request.NodeId.ToString("D"));
    return await existing.ExecuteScalarAsync(cancellationToken) is not null;
  }

  public async Task<RelayPollOutcome> PollAsync(
      Guid nodeId,
      string credential,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
    if (!await IsNodeCredentialValidAsync(connection, transaction, nodeId, credential, cancellationToken))
    {
      await transaction.RollbackAsync(cancellationToken);
      return new RelayPollOutcome(false, null);
    }
    await using (var updateActivity = connection.CreateCommand())
    {
      updateActivity.Transaction = transaction;
      updateActivity.CommandText =
          """
          UPDATE relay_nodes
          SET last_poll_at = CASE
              WHEN last_poll_at IS NULL OR last_poll_at < $lastPollAt
              THEN $lastPollAt
              ELSE last_poll_at
          END
          WHERE node_id = $nodeId
            AND revoked_at IS NULL;
          """;
      updateActivity.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      updateActivity.Parameters.AddWithValue("$lastPollAt", FormatUtc(now));
      await updateActivity.ExecuteNonQueryAsync(cancellationToken);
    }
    await using (var expire = connection.CreateCommand())
    {
      expire.Transaction = transaction;
      expire.CommandText =
          """
          UPDATE relay_sessions
          SET status = 'expired'
          WHERE node_id = $nodeId
            AND status IN ('queued', 'dispatched')
            AND expires_at < $now;
          """;
      expire.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      expire.Parameters.AddWithValue("$now", Format(now));
      await expire.ExecuteNonQueryAsync(cancellationToken);
    }
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            tenant_id,
            node_id,
            session_id,
            status,
            expires_at,
            request_envelope_json,
            result_envelope_json
        FROM relay_sessions
        WHERE node_id = $nodeId
          AND status IN ('queued', 'dispatched')
          AND expires_at >= $now
        ORDER BY expires_at, session_id
        LIMIT 1;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$now", Format(now));
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      await transaction.CommitAsync(cancellationToken);
      return new RelayPollOutcome(true, null);
    }
    var record = ReadRecord(reader);
    await reader.DisposeAsync();
    await using var update = connection.CreateCommand();
    update.Transaction = transaction;
    update.CommandText =
        """
        UPDATE relay_sessions
        SET status = 'dispatched'
        WHERE session_id = $sessionId
          AND status = 'queued';
        """;
    update.Parameters.AddWithValue("$sessionId", record.SessionId.ToString("D"));
    await update.ExecuteNonQueryAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return new RelayPollOutcome(
        true,
        record with { Status = "dispatched" });
  }

  public async Task<RelayResultUploadOutcome> UploadResultAsync(
      Guid nodeId,
      Guid sessionId,
      string credential,
      string resultEnvelope,
      DateTimeOffset completedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
    if (!await IsNodeCredentialValidAsync(connection, transaction, nodeId, credential, cancellationToken))
    {
      await transaction.RollbackAsync(cancellationToken);
      return RelayResultUploadOutcome.CredentialRejected;
    }
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE relay_sessions
        SET status = 'completed',
            result_envelope_json = $resultEnvelope
        WHERE node_id = $nodeId
          AND session_id = $sessionId
          AND status IN ('queued', 'dispatched');
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
    command.Parameters.AddWithValue("$resultEnvelope", resultEnvelope);
    var changed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    if (changed)
    {
      await using var updateActivity = connection.CreateCommand();
      updateActivity.Transaction = transaction;
      updateActivity.CommandText =
          """
          UPDATE relay_nodes
          SET last_result_at = CASE
              WHEN last_result_at IS NULL OR last_result_at < $lastResultAt
              THEN $lastResultAt
              ELSE last_result_at
          END
          WHERE node_id = $nodeId
            AND revoked_at IS NULL;
          """;
      updateActivity.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      updateActivity.Parameters.AddWithValue("$lastResultAt", FormatUtc(completedAt));
      await updateActivity.ExecuteNonQueryAsync(cancellationToken);
    }
    await transaction.CommitAsync(cancellationToken);
    return changed
        ? RelayResultUploadOutcome.Succeeded
        : RelayResultUploadOutcome.SessionRejected;
  }

  public async Task<IReadOnlyList<RelayNodeActivityRecord>> GetNodeActivityAsync(
      string tenantId,
      IReadOnlyList<Guid> nodeIds,
      CancellationToken cancellationToken)
  {
    if (nodeIds.Count == 0)
    {
      return [];
    }
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    var nodeParameters = new string[nodeIds.Count];
    for (var index = 0; index < nodeIds.Count; index++)
    {
      var parameterName = $"$nodeId{index}";
      nodeParameters[index] = parameterName;
      command.Parameters.AddWithValue(
          parameterName,
          nodeIds[index].ToString("D"));
    }
    command.CommandText =
        $"""
        SELECT node_id, last_poll_at, last_result_at
        FROM relay_nodes
        WHERE tenant_id = $tenantId
          AND node_id IN ({string.Join(", ", nodeParameters)})
        ORDER BY node_id;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    var activity = new List<RelayNodeActivityRecord>(nodeIds.Count);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      activity.Add(
          new RelayNodeActivityRecord(
              Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
              await reader.IsDBNullAsync(1, cancellationToken)
                  ? null
                  : DateTimeOffset.Parse(
                      reader.GetString(1),
                      CultureInfo.InvariantCulture),
              await reader.IsDBNullAsync(2, cancellationToken)
                  ? null
                  : DateTimeOffset.Parse(
                      reader.GetString(2),
                      CultureInfo.InvariantCulture)));
    }
    return activity;
  }

  public async Task<RelaySessionRecord?> GetSessionAsync(
      Guid sessionId,
      CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            tenant_id,
            node_id,
            session_id,
            status,
            expires_at,
            request_envelope_json,
            result_envelope_json
        FROM relay_sessions
        WHERE session_id = $sessionId;
        """;
    command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
  }

  public async Task<bool> CancelSessionAsync(
      Guid sessionId,
      CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE relay_sessions
        SET status = 'cancelled'
        WHERE session_id = $sessionId
          AND status IN ('queued', 'dispatched', 'cancelled');
        """;
    command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
  }

  private static async Task<RelayCredentialRotationStatus>
      GetCredentialRotationStatusAsync(
          SqliteConnection connection,
          SqliteTransaction transaction,
          Guid nodeId,
          RelayNodeCredentialRotationRequest request,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            node.tenant_id,
            node.transport_credential_hash,
            node.revoked_at,
            rotation.rotation_id,
            rotation.expected_transport_credential_hash,
            rotation.replacement_transport_credential_hash,
            rotation.phase,
            EXISTS (
                SELECT 1
                FROM relay_nodes AS duplicate
                WHERE duplicate.node_id <> $nodeId
                  AND duplicate.transport_credential_hash = $replacementHash)
            OR EXISTS (
                SELECT 1
                FROM relay_credential_rotations AS duplicate_rotation
                WHERE duplicate_rotation.node_id <> $nodeId
                  AND duplicate_rotation.replacement_transport_credential_hash =
                          $replacementHash)
        FROM relay_nodes AS node
        LEFT JOIN relay_credential_rotations AS rotation
          ON rotation.node_id = node.node_id
        WHERE node.node_id = $nodeId;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$replacementHash",
        request.ReplacementTransportCredentialHash);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return RelayCredentialRotationStatus.NotFound;
    }
    if (!string.Equals(
        reader.GetString(0),
        request.TenantId,
        StringComparison.Ordinal))
    {
      return RelayCredentialRotationStatus.Forbidden;
    }
    if (!await reader.IsDBNullAsync(2, cancellationToken))
    {
      return RelayCredentialRotationStatus.Revoked;
    }
    if (reader.GetBoolean(7))
    {
      return RelayCredentialRotationStatus.Conflict;
    }
    if (!await reader.IsDBNullAsync(3, cancellationToken))
    {
      var exact =
          Guid.Parse(
              reader.GetString(3),
              CultureInfo.InvariantCulture) == request.RotationId &&
          string.Equals(
              reader.GetString(4),
              request.ExpectedTransportCredentialHash,
              StringComparison.Ordinal) &&
          string.Equals(
              reader.GetString(5),
              request.ReplacementTransportCredentialHash,
              StringComparison.Ordinal);
      if (!exact)
      {
        if (!string.Equals(
            reader.GetString(6),
            "promoted",
            StringComparison.Ordinal))
        {
          return RelayCredentialRotationStatus.Conflict;
        }
      }
      else
      {
        return reader.GetString(6) switch
        {
          "prepared" => RelayCredentialRotationStatus.Prepared,
          "promoted" => RelayCredentialRotationStatus.Promoted,
          _ => RelayCredentialRotationStatus.Conflict,
        };
      }
    }
    return string.Equals(
        reader.GetString(1),
        request.ExpectedTransportCredentialHash,
        StringComparison.Ordinal)
        ? RelayCredentialRotationStatus.Authorized
        : RelayCredentialRotationStatus.Forbidden;
  }

  private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
  {
    var connection = new SqliteConnection($"Data Source={_databasePath};Foreign Keys=True");
    await connection.OpenAsync(cancellationToken);
    return connection;
  }

  private static async Task EnsureColumnAsync(
      SqliteConnection connection,
      string tableName,
      string columnName,
      string definition,
      CancellationToken cancellationToken)
  {
    await using var inspect = connection.CreateCommand();
    inspect.CommandText = $"PRAGMA table_info({tableName});";
    await using (var reader = await inspect.ExecuteReaderAsync(cancellationToken))
    {
      while (await reader.ReadAsync(cancellationToken))
      {
        if (string.Equals(
            reader.GetString(1),
            columnName,
            StringComparison.Ordinal))
        {
          return;
        }
      }
    }
    await using var alter = connection.CreateCommand();
    alter.CommandText =
        $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
    await alter.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<bool> IsNodeCredentialValidAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string credential,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT 1
        FROM relay_nodes AS node
        WHERE node.node_id = $nodeId
          AND node.revoked_at IS NULL
          AND (
              node.transport_credential_hash = $hash
              OR EXISTS (
                  SELECT 1
                  FROM relay_credential_rotations AS rotation
                  WHERE rotation.node_id = node.node_id
                    AND rotation.phase = 'prepared'
                    AND rotation.replacement_transport_credential_hash =
                            $hash));
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$hash", RelayCredentialHash.Hash(credential));
    return await command.ExecuteScalarAsync(cancellationToken) is not null;
  }

  private static RelaySessionRecord ReadRecord(SqliteDataReader reader) =>
      new(
          reader.GetString(0),
          Guid.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
          Guid.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
          reader.GetString(3),
          DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
          reader.GetString(5),
          reader.IsDBNull(6) ? null : reader.GetString(6));

  private static string Format(DateTimeOffset value) =>
      value.ToString("O", CultureInfo.InvariantCulture);

  private static string FormatUtc(DateTimeOffset value) =>
      value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
