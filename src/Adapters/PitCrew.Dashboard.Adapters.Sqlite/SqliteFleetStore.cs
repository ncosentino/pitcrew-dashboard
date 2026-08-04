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
      IFleetStorageTransaction storageTransaction,
      Guid nodeId,
      string connectorVersion,
      DateTimeOffset receivedAt,
      IReadOnlyList<ManagerObservedState> profiles,
      ConnectorCredentialUpdate credentialUpdate,
      CancellationToken cancellationToken)
  {
    var enlisted = SqliteFleetTransaction.Resolve(storageTransaction);
    var connection = enlisted.Connection;
    var transaction = enlisted.Transaction;

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
      var storedProfile = profile with { Host = null };
      var payload = JsonSerializer.Serialize(
          storedProfile,
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
  }

  public Task ApplyHostHardwareAsync(
      IFleetStorageTransaction transaction,
      Guid nodeId,
      IReadOnlyList<ManagerObservedState> profiles,
      IReadOnlyCollection<string> activeProfileIds,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(profiles);
    ArgumentNullException.ThrowIfNull(activeProfileIds);
    var enlisted = SqliteFleetTransaction.Resolve(transaction);
    return ApplyHostHardwareCoreAsync(
        enlisted.Connection,
        enlisted.Transaction,
        nodeId,
        profiles,
        activeProfileIds,
        receivedAt,
        cancellationToken);
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
                p.payload_json,
                h.status,
                h.collected_at,
                h.attempted_at,
                h.inventory_hash,
                h.processor_model,
                h.architecture,
                h.physical_core_count,
                h.logical_processor_count,
                h.performance_core_count,
                h.efficiency_core_count,
                h.memory_bytes,
                h.operating_system,
                h.kernel_version,
                h.docker_server_version,
                h.docker_storage_driver,
                h.docker_backing_filesystem
            FROM nodes AS n
            LEFT JOIN profiles AS p ON p.node_id = n.node_id
            LEFT JOIN node_hardware_current AS h
                ON h.node_id = n.node_id
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
            !await reader.IsDBNullAsync(6, cancellationToken),
            await ReadHostHardwareOrNullAsync(
                reader,
                cancellationToken));
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
          profilesByNode[pair.Key],
          [],
          [])
      {
        Hardware = row.Hardware,
      });
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
      bool CredentialRotationRequested,
      HostHardwareInventory? Hardware);

  private sealed record CurrentHardwareState(
      string Status,
      string? InventoryHash,
      string SourceProfileId,
      DateTimeOffset AttemptedAt);

  private static async Task ApplyHostHardwareCoreAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      IReadOnlyList<ManagerObservedState> profiles,
      IReadOnlyCollection<string> activeProfileIds,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken)
  {
    var acceptedProfileIds = profiles
        .Select(profile => profile.ProfileId)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var activeProfiles = activeProfileIds.ToHashSet(
        StringComparer.OrdinalIgnoreCase);
    if (acceptedProfileIds.Count == 0 && activeProfiles.Count > 0)
    {
      return;
    }
    CurrentHardwareState? previousCurrent = null;
    await using (var previous = connection.CreateCommand())
    {
      previous.Transaction = transaction;
      previous.CommandText =
          """
          SELECT
              status,
              inventory_hash,
              source_profile_id,
              attempted_at
          FROM node_hardware_current
          WHERE node_id = $nodeId;
          """;
      previous.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      await using var reader = await previous.ExecuteReaderAsync(
          cancellationToken);
      if (await reader.ReadAsync(cancellationToken))
      {
        previousCurrent = new CurrentHardwareState(
            reader.GetString(0),
            await reader.IsDBNullAsync(1, cancellationToken)
                ? null
                : reader.GetString(1),
            reader.GetString(2),
            ParseTimestamp(reader.GetString(3)));
      }
    }
    var candidate = profiles
        .Where(profile => profile.Host?.Hardware is not null)
        .Select(profile => (
            profile.ProfileId,
            Hardware: profile.Host!.Hardware))
        .OrderByDescending(item => HardwareStatusRank(
            item.Hardware.Status))
        .ThenByDescending(item => item.Hardware.AttemptedAt)
        .ThenBy(item => item.ProfileId, StringComparer.Ordinal)
        .FirstOrDefault();
    var preservePrevious = previousCurrent is not null &&
        activeProfiles.Contains(previousCurrent.SourceProfileId) &&
        !acceptedProfileIds.Contains(previousCurrent.SourceProfileId);
    if (candidate.Hardware is not null && preservePrevious)
    {
      var previousRank = HardwareStatusRank(previousCurrent!.Status);
      var candidateRank = HardwareStatusRank(candidate.Hardware.Status);
      if (previousRank > candidateRank ||
          previousRank == candidateRank &&
          (previousCurrent.AttemptedAt > candidate.Hardware.AttemptedAt ||
              previousCurrent.AttemptedAt ==
                  candidate.Hardware.AttemptedAt &&
              string.CompareOrdinal(
                  previousCurrent.SourceProfileId,
                  candidate.ProfileId) <= 0))
      {
        return;
      }
    }
    if (candidate.Hardware is null)
    {
      if (preservePrevious)
      {
        return;
      }
      await using var clear = connection.CreateCommand();
      clear.Transaction = transaction;
      clear.CommandText =
          """
          DELETE FROM node_hardware_current
          WHERE node_id = $nodeId;
          """;
      clear.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      await clear.ExecuteNonQueryAsync(cancellationToken);
      return;
    }

    await using var current = connection.CreateCommand();
    current.Transaction = transaction;
    current.CommandText =
        """
        INSERT INTO node_hardware_current (
            node_id,
            status,
            collected_at,
            attempted_at,
            inventory_hash,
            source_profile_id,
            processor_model,
            architecture,
            physical_core_count,
            logical_processor_count,
            performance_core_count,
            efficiency_core_count,
            memory_bytes,
            operating_system,
            kernel_version,
            docker_server_version,
            docker_storage_driver,
            docker_backing_filesystem,
            recorded_at)
        VALUES (
            $nodeId,
            $status,
            $collectedAt,
            $attemptedAt,
            $inventoryHash,
            $sourceProfileId,
            $processorModel,
            $architecture,
            $physicalCoreCount,
            $logicalProcessorCount,
            $performanceCoreCount,
            $efficiencyCoreCount,
            $memoryBytes,
            $operatingSystem,
            $kernelVersion,
            $dockerServerVersion,
            $dockerStorageDriver,
            $dockerBackingFilesystem,
            $recordedAt)
        ON CONFLICT (node_id) DO UPDATE SET
            status = excluded.status,
            collected_at = excluded.collected_at,
            attempted_at = excluded.attempted_at,
            inventory_hash = excluded.inventory_hash,
            source_profile_id = excluded.source_profile_id,
            processor_model = excluded.processor_model,
            architecture = excluded.architecture,
            physical_core_count = excluded.physical_core_count,
            logical_processor_count = excluded.logical_processor_count,
            performance_core_count = excluded.performance_core_count,
            efficiency_core_count = excluded.efficiency_core_count,
            memory_bytes = excluded.memory_bytes,
            operating_system = excluded.operating_system,
            kernel_version = excluded.kernel_version,
            docker_server_version = excluded.docker_server_version,
            docker_storage_driver = excluded.docker_storage_driver,
            docker_backing_filesystem =
                excluded.docker_backing_filesystem,
            recorded_at = excluded.recorded_at
        WHERE excluded.recorded_at >=
            node_hardware_current.recorded_at;
        """;
    AddHostHardwareParameters(
        current,
        nodeId,
        candidate.ProfileId,
        candidate.Hardware,
        receivedAt);
    await current.ExecuteNonQueryAsync(cancellationToken);

    if (candidate.Hardware.InventoryHash is null ||
        candidate.Hardware.CollectedAt is null)
    {
      return;
    }
    long? latestRevisionId = null;
    string? latestInventoryHash = null;
    await using (var latestRevision = connection.CreateCommand())
    {
      latestRevision.Transaction = transaction;
      latestRevision.CommandText =
          """
          SELECT revision_id, inventory_hash
          FROM node_hardware_revisions
          WHERE node_id = $nodeId
          ORDER BY last_observed_at DESC, revision_id DESC
          LIMIT 1;
          """;
      latestRevision.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      await using var reader = await latestRevision.ExecuteReaderAsync(
          cancellationToken);
      if (await reader.ReadAsync(cancellationToken))
      {
        latestRevisionId = reader.GetInt64(0);
        latestInventoryHash = reader.GetString(1);
      }
    }
    if (previousCurrent is not null &&
        previousCurrent.Status is not "unavailable" &&
        string.Equals(
            previousCurrent.InventoryHash,
            candidate.Hardware.InventoryHash,
            StringComparison.Ordinal) &&
        latestRevisionId is not null &&
        string.Equals(
            latestInventoryHash,
            candidate.Hardware.InventoryHash,
            StringComparison.Ordinal))
    {
      await using var refresh = connection.CreateCommand();
      refresh.Transaction = transaction;
      refresh.CommandText =
          """
          UPDATE node_hardware_revisions
          SET last_observed_at = $recordedAt,
              last_status = $status,
              last_attempted_at = $attemptedAt,
              source_profile_id = $sourceProfileId
          WHERE revision_id = $revisionId
            AND last_observed_at <= $recordedAt;
          """;
      refresh.Parameters.AddWithValue(
          "$recordedAt",
          FormatTimestamp(receivedAt));
      refresh.Parameters.AddWithValue(
          "$status",
          candidate.Hardware.Status);
      refresh.Parameters.AddWithValue(
          "$attemptedAt",
          FormatTimestamp(candidate.Hardware.AttemptedAt));
      refresh.Parameters.AddWithValue(
          "$sourceProfileId",
          candidate.ProfileId);
      refresh.Parameters.AddWithValue(
          "$revisionId",
          latestRevisionId.Value);
      await refresh.ExecuteNonQueryAsync(cancellationToken);
      return;
    }
    await using var revision = connection.CreateCommand();
    revision.Transaction = transaction;
    revision.CommandText =
        """
        INSERT INTO node_hardware_revisions (
            node_id,
            inventory_hash,
            collected_at,
            first_observed_at,
            last_observed_at,
            last_status,
            last_attempted_at,
            source_profile_id,
            processor_model,
            architecture,
            physical_core_count,
            logical_processor_count,
            performance_core_count,
            efficiency_core_count,
            memory_bytes,
            operating_system,
            kernel_version,
            docker_server_version,
            docker_storage_driver,
            docker_backing_filesystem)
        VALUES (
            $nodeId,
            $inventoryHash,
            $collectedAt,
            $recordedAt,
            $recordedAt,
            $status,
            $attemptedAt,
            $sourceProfileId,
            $processorModel,
            $architecture,
            $physicalCoreCount,
            $logicalProcessorCount,
            $performanceCoreCount,
            $efficiencyCoreCount,
            $memoryBytes,
            $operatingSystem,
            $kernelVersion,
            $dockerServerVersion,
            $dockerStorageDriver,
            $dockerBackingFilesystem);
        """;
    AddHostHardwareParameters(
        revision,
        nodeId,
        candidate.ProfileId,
        candidate.Hardware,
        receivedAt);
    await revision.ExecuteNonQueryAsync(cancellationToken);
  }

  private static void AddHostHardwareParameters(
      SqliteCommand command,
      Guid nodeId,
      string profileId,
      HostHardwareInventory hardware,
      DateTimeOffset recordedAt)
  {
    command.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    command.Parameters.AddWithValue("$status", hardware.Status);
    command.Parameters.AddWithValue(
        "$collectedAt",
        hardware.CollectedAt is null
            ? DBNull.Value
            : FormatTimestamp(hardware.CollectedAt.Value));
    command.Parameters.AddWithValue(
        "$attemptedAt",
        FormatTimestamp(hardware.AttemptedAt));
    command.Parameters.AddWithValue(
        "$inventoryHash",
        (object?)hardware.InventoryHash ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$sourceProfileId",
        profileId);
    command.Parameters.AddWithValue(
        "$processorModel",
        (object?)hardware.ProcessorModel ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$architecture",
        (object?)hardware.Architecture ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$physicalCoreCount",
        (object?)hardware.PhysicalCoreCount ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$logicalProcessorCount",
        (object?)hardware.LogicalProcessorCount ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$performanceCoreCount",
        (object?)hardware.PerformanceCoreCount ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$efficiencyCoreCount",
        (object?)hardware.EfficiencyCoreCount ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$memoryBytes",
        (object?)hardware.MemoryBytes ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$operatingSystem",
        (object?)hardware.OperatingSystem ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$kernelVersion",
        (object?)hardware.KernelVersion ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$dockerServerVersion",
        (object?)hardware.DockerServerVersion ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$dockerStorageDriver",
        (object?)hardware.DockerStorageDriver ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$dockerBackingFilesystem",
        (object?)hardware.DockerBackingFilesystem ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$recordedAt",
        FormatTimestamp(recordedAt));
  }

  private static async Task<HostHardwareInventory?>
      ReadHostHardwareOrNullAsync(
          SqliteDataReader reader,
          CancellationToken cancellationToken)
  {
    if (await reader.IsDBNullAsync(8, cancellationToken))
    {
      return null;
    }
    return new HostHardwareInventory(
        reader.GetString(8),
        await reader.IsDBNullAsync(9, cancellationToken)
            ? null
            : ParseTimestamp(reader.GetString(9)),
        ParseTimestamp(reader.GetString(10)),
        await reader.IsDBNullAsync(11, cancellationToken)
            ? null
            : reader.GetString(11),
        await OptionalStringAsync(reader, 12, cancellationToken),
        await OptionalStringAsync(reader, 13, cancellationToken),
        await OptionalInt64Async(reader, 14, cancellationToken),
        await OptionalInt64Async(reader, 15, cancellationToken),
        await OptionalInt64Async(reader, 16, cancellationToken),
        await OptionalInt64Async(reader, 17, cancellationToken),
        await OptionalInt64Async(reader, 18, cancellationToken),
        await OptionalStringAsync(reader, 19, cancellationToken),
        await OptionalStringAsync(reader, 20, cancellationToken),
        await OptionalStringAsync(reader, 21, cancellationToken),
        await OptionalStringAsync(reader, 22, cancellationToken),
        await OptionalStringAsync(reader, 23, cancellationToken));
  }

  private static async Task<string?> OptionalStringAsync(
      SqliteDataReader reader,
      int ordinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(ordinal, cancellationToken)
          ? null
          : reader.GetString(ordinal);

  private static async Task<long?> OptionalInt64Async(
      SqliteDataReader reader,
      int ordinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(ordinal, cancellationToken)
          ? null
          : reader.GetInt64(ordinal);

  private static int HardwareStatusRank(string status) =>
      status switch
      {
        "current" => 2,
        "stale" => 1,
        _ => 0,
      };

  private static string FormatTimestamp(DateTimeOffset value) =>
      value.ToUniversalTime().ToString(
          "O",
          CultureInfo.InvariantCulture);

  private static DateTimeOffset ParseTimestamp(string value) =>
      DateTimeOffset.Parse(
          value,
          CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind);
}
