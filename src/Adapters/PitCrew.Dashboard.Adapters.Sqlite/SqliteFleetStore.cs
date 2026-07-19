using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed class SqliteFleetStore(
    SqliteConnectionFactory _connectionFactory,
    SqliteMigrationRunner _migrationRunner) : IFleetStore
{
  public Task InitializeAsync(CancellationToken cancellationToken) =>
      _migrationRunner.ApplyAsync(cancellationToken);

  public async Task<Guid> EnrollNodeAsync(
      string tenantId,
      string connectorInstanceId,
      string displayName,
      string credentialHash,
      DateTimeOffset enrolledAt,
      CancellationToken cancellationToken)
  {
    var candidateNodeId = Guid.NewGuid();
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
            INSERT INTO tenants (tenant_id)
            VALUES ($tenantId)
            ON CONFLICT (tenant_id) DO NOTHING;

            INSERT INTO nodes (
                node_id,
                tenant_id,
                connector_instance_id,
                display_name,
                credential_hash,
                enrolled_at)
            VALUES (
                $nodeId,
                $tenantId,
                $connectorInstanceId,
                $displayName,
                $credentialHash,
                $enrolledAt)
            ON CONFLICT (tenant_id, connector_instance_id) DO UPDATE SET
                display_name = excluded.display_name,
                credential_hash = excluded.credential_hash
            RETURNING node_id;
            """;
    command.Parameters.AddWithValue("$nodeId", candidateNodeId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$connectorInstanceId", connectorInstanceId);
    command.Parameters.AddWithValue("$displayName", displayName);
    command.Parameters.AddWithValue("$credentialHash", credentialHash);
    command.Parameters.AddWithValue("$enrolledAt", enrolledAt.ToString("O", CultureInfo.InvariantCulture));
    var nodeIdText = Convert.ToString(
        await command.ExecuteScalarAsync(cancellationToken),
        CultureInfo.InvariantCulture);
    await transaction.CommitAsync(cancellationToken);

    if (!Guid.TryParse(
        nodeIdText,
        CultureInfo.InvariantCulture,
        out var nodeId))
    {
      throw new InvalidOperationException("SQLite did not return a valid node identifier.");
    }

    return nodeId;
  }

  public async Task<ConnectorNodeIdentity?> ResolveNodeOrNullAsync(
      string credentialHash,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
            SELECT node_id, tenant_id
            FROM nodes
            WHERE credential_hash = $credentialHash;
            """;
    command.Parameters.AddWithValue("$credentialHash", credentialHash);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return null;
    }

    return new ConnectorNodeIdentity(
        Guid.Parse(
            reader.GetString(0),
            CultureInfo.InvariantCulture),
        reader.GetString(1));
  }

  public async Task ApplySyncAsync(
      Guid nodeId,
      string connectorVersion,
      DateTimeOffset receivedAt,
      IReadOnlyList<ManagerObservedState> profiles,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;

    var sql = new System.Text.StringBuilder(
        """
            UPDATE nodes
            SET connector_version = $connectorVersion,
                last_seen_at = $receivedAt
            WHERE node_id = $nodeId;
            """);
    command.Parameters.AddWithValue("$connectorVersion", connectorVersion);
    command.Parameters.AddWithValue(
        "$receivedAt",
        receivedAt.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));

    if (profiles.Count == 0)
    {
      sql.AppendLine("DELETE FROM profiles WHERE node_id = $nodeId;");
    }
    else
    {
      var profileParameters = new string[profiles.Count];
      for (var index = 0; index < profiles.Count; index++)
      {
        profileParameters[index] = $"$profileId{index}";
        command.Parameters.AddWithValue(profileParameters[index], profiles[index].ProfileId);
      }

      sql.AppendLine(
          $"DELETE FROM profiles WHERE node_id = $nodeId AND profile_id NOT IN ({string.Join(", ", profileParameters)});");
    }

    for (var index = 0; index < profiles.Count; index++)
    {
      var profile = profiles[index];
      var payload = JsonSerializer.Serialize(
          profile,
          PitCrewProtocolJsonContext.Default.ManagerObservedState);
      var payloadHash = Convert.ToHexString(
          SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
      sql.AppendLine(
          $"""
                INSERT INTO profiles (
                    node_id,
                    profile_id,
                    payload_hash,
                    payload_json,
                    observed_at)
                VALUES (
                    $nodeId,
                    $profileId{index},
                    $payloadHash{index},
                    $payloadJson{index},
                    $observedAt{index})
                ON CONFLICT (node_id, profile_id) DO UPDATE SET
                    payload_hash = excluded.payload_hash,
                    payload_json = excluded.payload_json,
                    observed_at = excluded.observed_at
                WHERE profiles.payload_hash <> excluded.payload_hash;
                """);
      command.Parameters.AddWithValue($"$payloadHash{index}", payloadHash);
      command.Parameters.AddWithValue($"$payloadJson{index}", payload);
      command.Parameters.AddWithValue(
          $"$observedAt{index}",
          profile.ObservedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    command.CommandText = sql.ToString();
    var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
    if (affectedRows == 0)
    {
      throw new InvalidOperationException(
          $"Node '{nodeId}' was not updated because it is no longer enrolled.");
    }

    await transaction.CommitAsync(cancellationToken);
  }

  public async Task<FleetResponse> GetFleetAsync(
      string tenantId,
      DateTimeOffset generatedAt,
      TimeSpan onlineWindow,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
            SELECT
                n.node_id,
                n.display_name,
                n.connector_version,
                n.enrolled_at,
                n.last_seen_at,
                p.payload_json
            FROM nodes AS n
            LEFT JOIN profiles AS p ON p.node_id = n.node_id
            WHERE n.tenant_id = $tenantId
            ORDER BY n.display_name, p.profile_id;
            """;
    command.Parameters.AddWithValue("$tenantId", tenantId);

    var nodes = new List<FleetNode>();
    var profilesByNode = new Dictionary<Guid, List<ManagerObservedState>>();
    var nodeRows = new Dictionary<Guid, NodeRow>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      var nodeId = Guid.Parse(
          reader.GetString(0),
          CultureInfo.InvariantCulture);
      if (!nodeRows.ContainsKey(nodeId))
      {
        nodeRows[nodeId] = new NodeRow(
            reader.GetString(1),
            reader.GetString(2),
            DateTimeOffset.Parse(
                reader.GetString(3),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            await reader.IsDBNullAsync(4, cancellationToken)
                ? null
                : DateTimeOffset.Parse(
                    reader.GetString(4),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind));
        profilesByNode[nodeId] = [];
      }

      if (!await reader.IsDBNullAsync(5, cancellationToken))
      {
        var profile = JsonSerializer.Deserialize(
            reader.GetString(5),
            PitCrewProtocolJsonContext.Default.ManagerObservedState);
        if (profile is null)
        {
          throw new InvalidOperationException(
              $"Stored profile projection for node '{nodeId}' could not be deserialized.");
        }
        profilesByNode[nodeId].Add(profile);
      }
    }

    foreach (var pair in nodeRows)
    {
      var row = pair.Value;
      var isOnline = row.LastSeenAt is not null &&
          generatedAt - row.LastSeenAt.Value <= onlineWindow;
      nodes.Add(new FleetNode(
          pair.Key,
          row.DisplayName,
          row.ConnectorVersion,
          row.EnrolledAt,
          row.LastSeenAt,
          isOnline,
          profilesByNode[pair.Key]));
    }

    return new FleetResponse(generatedAt, nodes);
  }

  private sealed record NodeRow(
      string DisplayName,
      string ConnectorVersion,
      DateTimeOffset EnrolledAt,
      DateTimeOffset? LastSeenAt);
}
