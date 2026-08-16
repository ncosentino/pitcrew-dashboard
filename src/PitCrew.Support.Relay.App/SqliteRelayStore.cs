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
            revoked_at TEXT NULL
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
        """;
    await command.ExecuteNonQueryAsync(cancellationToken);
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

  public async Task<RelaySessionRecord?> PollAsync(
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
      return null;
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
      return null;
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
    return record with { Status = "dispatched" };
  }

  public async Task<bool> UploadResultAsync(
      Guid nodeId,
      Guid sessionId,
      string credential,
      string resultEnvelope,
      CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken);
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
    if (!await IsNodeCredentialValidAsync(connection, transaction, nodeId, credential, cancellationToken))
    {
      await transaction.RollbackAsync(cancellationToken);
      return false;
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
    await transaction.CommitAsync(cancellationToken);
    return changed;
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

  private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
  {
    var connection = new SqliteConnection($"Data Source={_databasePath};Foreign Keys=True");
    await connection.OpenAsync(cancellationToken);
    return connection;
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
        FROM relay_nodes
        WHERE node_id = $nodeId
          AND transport_credential_hash = $hash
          AND revoked_at IS NULL;
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
}
