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
    SqliteConnectionFactory _connectionFactory) : IFleetStore
{
  public async Task CreateEnrollmentCodeAsync(
      Guid enrollmentCodeId,
      string tenantId,
      string codeHash,
      string label,
      string createdByGitHubUserId,
      DateTimeOffset createdAt,
      DateTimeOffset expiresAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        DELETE FROM enrollment_codes
        WHERE expires_at < $createdAt;

        INSERT INTO enrollment_codes (
            enrollment_code_id,
            tenant_id,
            code_hash,
            label,
            created_by_github_user_id,
            created_at,
            expires_at)
        VALUES (
            $enrollmentCodeId,
            $tenantId,
            $codeHash,
            $label,
            $createdByGitHubUserId,
            $createdAt,
            $expiresAt);
        """;
    command.Parameters.AddWithValue(
        "$enrollmentCodeId",
        enrollmentCodeId.ToString("D"));
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue("$codeHash", codeHash);
    command.Parameters.AddWithValue("$label", label);
    command.Parameters.AddWithValue(
        "$createdByGitHubUserId",
        createdByGitHubUserId);
    command.Parameters.AddWithValue(
        "$createdAt",
        createdAt.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue(
        "$expiresAt",
        expiresAt.ToString("O", CultureInfo.InvariantCulture));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  public async Task<ConnectorEnrollmentCommit> RedeemEnrollmentCodeAsync(
      string codeHash,
      string connectorInstanceId,
      string displayName,
      string credentialHash,
      DateTimeOffset redeemedAt,
      CancellationToken cancellationToken)
  {
    var candidateNodeId = Guid.NewGuid();
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    await using var codeCommand = connection.CreateCommand();
    codeCommand.Transaction = transaction;
    codeCommand.CommandText =
        """
        SELECT enrollment_code_id, tenant_id
        FROM enrollment_codes
        WHERE code_hash = $codeHash
          AND consumed_at IS NULL
          AND expires_at >= $redeemedAt;
        """;
    codeCommand.Parameters.AddWithValue("$codeHash", codeHash);
    codeCommand.Parameters.AddWithValue(
        "$redeemedAt",
        redeemedAt.ToString("O", CultureInfo.InvariantCulture));
    await using var codeReader = await codeCommand.ExecuteReaderAsync(
        cancellationToken);
    if (!await codeReader.ReadAsync(cancellationToken))
    {
      await transaction.RollbackAsync(cancellationToken);
      return new ConnectorEnrollmentCommit(
          ConnectorEnrollmentStatus.InvalidCode,
          null);
    }
    var enrollmentCodeId = codeReader.GetString(0);
    var tenantId = codeReader.GetString(1);
    await codeReader.DisposeAsync();

    await using var nodeCommand = connection.CreateCommand();
    nodeCommand.Transaction = transaction;
    nodeCommand.CommandText =
        """
        INSERT INTO nodes (
            node_id,
            tenant_id,
            connector_instance_id,
            display_name,
            credential_hash,
            enrolled_at,
            revoked_at,
            rotation_requested_at,
            pending_credential_hash,
            credential_rotated_at)
        VALUES (
            $nodeId,
            $tenantId,
            $connectorInstanceId,
            $displayName,
            $credentialHash,
            $redeemedAt,
            NULL,
            NULL,
            NULL,
            $redeemedAt)
        ON CONFLICT (tenant_id, connector_instance_id) DO UPDATE SET
            display_name = excluded.display_name,
            credential_hash = excluded.credential_hash,
            revoked_at = NULL,
            rotation_requested_at = NULL,
            pending_credential_hash = NULL,
            credential_rotated_at = excluded.credential_rotated_at
        RETURNING node_id;
        """;
    nodeCommand.Parameters.AddWithValue(
        "$nodeId",
        candidateNodeId.ToString("D"));
    nodeCommand.Parameters.AddWithValue("$tenantId", tenantId);
    nodeCommand.Parameters.AddWithValue(
        "$connectorInstanceId",
        connectorInstanceId);
    nodeCommand.Parameters.AddWithValue("$displayName", displayName);
    nodeCommand.Parameters.AddWithValue(
        "$credentialHash",
        credentialHash);
    nodeCommand.Parameters.AddWithValue(
        "$redeemedAt",
        redeemedAt.ToString("O", CultureInfo.InvariantCulture));
    var nodeIdText = Convert.ToString(
        await nodeCommand.ExecuteScalarAsync(cancellationToken),
        CultureInfo.InvariantCulture);
    if (!Guid.TryParse(
        nodeIdText,
        CultureInfo.InvariantCulture,
        out var nodeId))
    {
      throw new InvalidOperationException("SQLite did not return a valid node identifier.");
    }

    await using var consumeCommand = connection.CreateCommand();
    consumeCommand.Transaction = transaction;
    consumeCommand.CommandText =
        """
        UPDATE enrollment_codes
        SET consumed_at = $redeemedAt,
            consumed_by_node_id = $nodeId
        WHERE enrollment_code_id = $enrollmentCodeId
          AND consumed_at IS NULL;
        """;
    consumeCommand.Parameters.AddWithValue(
        "$redeemedAt",
        redeemedAt.ToString("O", CultureInfo.InvariantCulture));
    consumeCommand.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    consumeCommand.Parameters.AddWithValue(
        "$enrollmentCodeId",
        enrollmentCodeId);
    if (await consumeCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
    {
      await transaction.RollbackAsync(cancellationToken);
      return new ConnectorEnrollmentCommit(
          ConnectorEnrollmentStatus.InvalidCode,
          null);
    }

    await transaction.CommitAsync(cancellationToken);
    return new ConnectorEnrollmentCommit(
        ConnectorEnrollmentStatus.Accepted,
        nodeId);
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
        SELECT
            node_id,
            tenant_id,
            CASE
                WHEN pending_credential_hash = $credentialHash
                    THEN 'pending'
                ELSE 'current'
            END,
            rotation_requested_at IS NOT NULL
        FROM nodes
        WHERE revoked_at IS NULL
          AND (
              credential_hash = $credentialHash
              OR pending_credential_hash = $credentialHash);
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
        reader.GetString(1),
        string.Equals(
            reader.GetString(2),
            "pending",
            StringComparison.Ordinal)
            ? ConnectorCredentialSlot.Pending
            : ConnectorCredentialSlot.Current,
        reader.GetBoolean(3));
  }

  public async Task ApplySyncAsync(
      Guid nodeId,
      string connectorVersion,
      DateTimeOffset receivedAt,
      IReadOnlyList<ManagerObservedState> profiles,
      ConnectorCredentialUpdate credentialUpdate,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);

    await using (var nodeCommand = connection.CreateCommand())
    {
      nodeCommand.Transaction = transaction;
      nodeCommand.CommandText = credentialUpdate.Kind switch
      {
        ConnectorCredentialUpdateKind.None =>
            """
            UPDATE nodes
            SET connector_version = $connectorVersion,
                last_seen_at = $receivedAt
            WHERE node_id = $nodeId
              AND revoked_at IS NULL;
            """,
        ConnectorCredentialUpdateKind.Stage =>
            """
            UPDATE nodes
            SET connector_version = $connectorVersion,
                last_seen_at = $receivedAt,
                pending_credential_hash = $credentialHash
            WHERE node_id = $nodeId
              AND revoked_at IS NULL;
            """,
        ConnectorCredentialUpdateKind.Promote =>
            """
            UPDATE nodes
            SET connector_version = $connectorVersion,
                last_seen_at = $receivedAt,
                credential_hash = pending_credential_hash,
                pending_credential_hash = NULL,
                rotation_requested_at = NULL,
                credential_rotated_at = $receivedAt
            WHERE node_id = $nodeId
              AND revoked_at IS NULL
              AND pending_credential_hash = $credentialHash;
            """,
        _ => throw new ArgumentOutOfRangeException(
            nameof(credentialUpdate)),
      };
      nodeCommand.Parameters.AddWithValue(
          "$connectorVersion",
          connectorVersion);
      nodeCommand.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      nodeCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      nodeCommand.Parameters.AddWithValue(
          "$credentialHash",
          credentialUpdate.CredentialHash);
      if (await nodeCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        throw new InvalidOperationException(
            $"Node '{nodeId}' was not updated because its credential state changed.");
      }
    }

    await using var profileCommand = connection.CreateCommand();
    profileCommand.Transaction = transaction;
    var sql = new System.Text.StringBuilder();
    profileCommand.Parameters.AddWithValue(
        "$receivedAt",
        receivedAt.ToString("O", CultureInfo.InvariantCulture));
    profileCommand.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));

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
        profileCommand.Parameters.AddWithValue(
            profileParameters[index],
            profiles[index].ProfileId);
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
      profileCommand.Parameters.AddWithValue(
          $"$payloadHash{index}",
          payloadHash);
      profileCommand.Parameters.AddWithValue(
          $"$payloadJson{index}",
          payload);
      profileCommand.Parameters.AddWithValue(
          $"$observedAt{index}",
          profile.ObservedAt.ToString(
              "O",
              CultureInfo.InvariantCulture));
    }

    profileCommand.CommandText = sql.ToString();
    if (profileCommand.CommandText.Length > 0)
    {
      await profileCommand.ExecuteNonQueryAsync(cancellationToken);
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
                COALESCE(
                    n.display_name_override,
                    n.display_name),
                n.connector_version,
                n.enrolled_at,
                n.last_seen_at,
                n.revoked_at,
                n.rotation_requested_at,
                p.payload_json
            FROM nodes AS n
            LEFT JOIN profiles AS p ON p.node_id = n.node_id
            WHERE n.tenant_id = $tenantId
            ORDER BY
                COALESCE(
                    n.display_name_override,
                    n.display_name),
                p.profile_id;
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
                    DateTimeStyles.RoundtripKind),
            !await reader.IsDBNullAsync(5, cancellationToken),
            !await reader.IsDBNullAsync(6, cancellationToken));
        profilesByNode[nodeId] = [];
      }

      if (!await reader.IsDBNullAsync(7, cancellationToken))
      {
        var profile = JsonSerializer.Deserialize(
            reader.GetString(7),
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
      var isOnline = !row.IsRevoked &&
          row.LastSeenAt is not null &&
          generatedAt - row.LastSeenAt.Value <= onlineWindow;
      nodes.Add(new FleetNode(
          pair.Key,
          row.DisplayName,
          row.ConnectorVersion,
          row.EnrolledAt,
          row.LastSeenAt,
          isOnline,
          row.IsRevoked,
          row.CredentialRotationRequested,
          profilesByNode[pair.Key]));
    }

    return new FleetResponse(generatedAt, nodes);
  }

  public async Task<NodeMutationStatus> RenameNodeAsync(
      string tenantId,
      Guid nodeId,
      string displayName,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE nodes
        SET display_name_override = $displayName
        WHERE tenant_id = $tenantId
          AND node_id = $nodeId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$displayName",
        displayName);
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1
        ? NodeMutationStatus.Succeeded
        : NodeMutationStatus.NotFound;
  }

  public async Task<NodeMutationStatus> RevokeNodeAsync(
      string tenantId,
      Guid nodeId,
      DateTimeOffset revokedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE nodes
        SET revoked_at = $revokedAt,
            rotation_requested_at = NULL,
            pending_credential_hash = NULL
        WHERE tenant_id = $tenantId
          AND node_id = $nodeId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$revokedAt",
        revokedAt.ToString("O", CultureInfo.InvariantCulture));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1
        ? NodeMutationStatus.Succeeded
        : NodeMutationStatus.NotFound;
  }

  public async Task<NodeMutationStatus> RequestCredentialRotationAsync(
      string tenantId,
      Guid nodeId,
      DateTimeOffset requestedAt,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE nodes
        SET rotation_requested_at =
                COALESCE(rotation_requested_at, $requestedAt)
        WHERE tenant_id = $tenantId
          AND node_id = $nodeId
          AND revoked_at IS NULL;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    command.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$requestedAt",
        requestedAt.ToString("O", CultureInfo.InvariantCulture));
    if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
    {
      return NodeMutationStatus.Succeeded;
    }

    await using var statusCommand = connection.CreateCommand();
    statusCommand.CommandText =
        """
        SELECT revoked_at IS NOT NULL
        FROM nodes
        WHERE tenant_id = $tenantId
          AND node_id = $nodeId;
        """;
    statusCommand.Parameters.AddWithValue("$tenantId", tenantId);
    statusCommand.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    var revoked = await statusCommand.ExecuteScalarAsync(
        cancellationToken);
    return revoked is null
        ? NodeMutationStatus.NotFound
        : Convert.ToBoolean(
            revoked,
            CultureInfo.InvariantCulture)
            ? NodeMutationStatus.Revoked
            : NodeMutationStatus.NotFound;
  }

  private sealed record NodeRow(
      string DisplayName,
      string ConnectorVersion,
      DateTimeOffset EnrolledAt,
      DateTimeOffset? LastSeenAt,
      bool IsRevoked,
      bool CredentialRotationRequested);
}
