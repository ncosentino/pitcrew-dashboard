using System.Globalization;
using System.Text.Json;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteRecoveryCommandStore(
    SqliteConnectionFactory _connectionFactory) : IRecoveryCommandStore
{
  public async Task<RecoveryCommandQueueResult> QueueAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      RecoveryCommandFences fences,
      string requestedByGitHubUserId,
      DateTimeOffset requestedAt,
      DateTimeOffset expiresAt,
      DateTimeOffset capabilityObservedAfter,
      DateTimeOffset repeatAllowedAfter,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);

    RecoveryOperatorCapability? capability;
    DateTimeOffset? capabilityAt;
    await using (var capabilityCommand = connection.CreateCommand())
    {
      capabilityCommand.Transaction = transaction;
      capabilityCommand.CommandText =
          """
          SELECT recovery_capability_json, recovery_capability_at
          FROM nodes
          WHERE tenant_id = $tenantId
            AND node_id = $nodeId
            AND revoked_at IS NULL;
          """;
      capabilityCommand.Parameters.AddWithValue("$tenantId", tenantId);
      capabilityCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      await using var reader = await capabilityCommand.ExecuteReaderAsync(
          cancellationToken);
      if (!await reader.ReadAsync(cancellationToken))
      {
        return new RecoveryCommandQueueResult(
            RecoveryCommandQueueStatus.NodeNotFound,
            null);
      }
      capability = await reader.IsDBNullAsync(0, cancellationToken)
          ? null
          : JsonSerializer.Deserialize(
              reader.GetString(0),
              PitCrewProtocolJsonContext.Default.RecoveryOperatorCapability);
      capabilityAt = await reader.IsDBNullAsync(1, cancellationToken)
          ? null
          : DateTimeOffset.Parse(
              reader.GetString(1),
              CultureInfo.InvariantCulture,
              DateTimeStyles.RoundtripKind);
    }

    var profile = capability?.Profiles.FirstOrDefault(candidate =>
        string.Equals(
            candidate.ProfileId,
            profileId,
            StringComparison.OrdinalIgnoreCase));
    if (profile is null)
    {
      return new RecoveryCommandQueueResult(
          RecoveryCommandQueueStatus.Unsupported,
          null);
    }

    var maximumObservedAgeSeconds =
        (requestedAt - capabilityObservedAfter).TotalSeconds;
    if (capabilityAt is null ||
        capabilityAt < capabilityObservedAfter ||
        profile.ObservedStateAgeSeconds > maximumObservedAgeSeconds)
    {
      return new RecoveryCommandQueueResult(
          RecoveryCommandQueueStatus.StaleFence,
          null);
    }
    if (!profile.ManagerContractSupported ||
        !profile.RecoveryAllowed ||
        !profile.SingleManagerResolved)
    {
      return new RecoveryCommandQueueResult(
          RecoveryCommandQueueStatus.NotAllowed,
          null);
    }
    if (profile.OperationActive)
    {
      return new RecoveryCommandQueueResult(
          RecoveryCommandQueueStatus.Conflict,
          null);
    }
    if (profile.ExpectedManagerInstanceId is null ||
        !string.Equals(
            profile.ExpectedManagerInstanceId,
            fences.ExpectedManagerInstanceId,
            StringComparison.Ordinal) ||
        profile.DesiredGeneration != fences.ExpectedGeneration ||
        !string.Equals(
            profile.DesiredStateHash,
            fences.ExpectedDesiredStateHash,
            StringComparison.OrdinalIgnoreCase))
    {
      return new RecoveryCommandQueueResult(
          RecoveryCommandQueueStatus.StaleFence,
          null);
    }

    await using (var recentCommand = connection.CreateCommand())
    {
      recentCommand.Transaction = transaction;
      recentCommand.CommandText =
          """
          SELECT 1
          FROM recovery_commands
          WHERE node_id = $nodeId
            AND profile_id = $profileId
            AND requested_at > $repeatAllowedAfter
          LIMIT 1;
          """;
      recentCommand.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      recentCommand.Parameters.AddWithValue("$profileId", profile.ProfileId);
      recentCommand.Parameters.AddWithValue(
          "$repeatAllowedAfter",
          repeatAllowedAfter.ToString("O", CultureInfo.InvariantCulture));
      if (await recentCommand.ExecuteScalarAsync(cancellationToken) is not null)
      {
        return new RecoveryCommandQueueResult(
            RecoveryCommandQueueStatus.RateLimited,
            null);
      }
    }

    var localExpiry = requestedAt.AddSeconds(profile.MaximumExpirySeconds);
    var effectiveExpiry = expiresAt < localExpiry
        ? expiresAt
        : localExpiry;
    var commandId = Guid.NewGuid();
    if (!await SqliteProfileOperationSlot.AcquireAsync(
        connection,
        transaction,
        nodeId,
        profile.ProfileId,
        SqliteProfileOperationSlot.RecoveryKind,
        commandId,
        requestedAt,
        cancellationToken))
    {
      return new RecoveryCommandQueueResult(
          RecoveryCommandQueueStatus.Conflict,
          null);
    }

    await using (var insert = connection.CreateCommand())
    {
      insert.Transaction = transaction;
      insert.CommandText =
          """
          INSERT INTO recovery_commands (
              command_id,
              node_id,
              profile_id,
              expected_manager_instance_id,
              expected_generation,
              expected_desired_state_hash,
              status,
              requested_by_github_user_id,
              requested_at,
              expires_at)
          VALUES (
              $commandId,
              $nodeId,
              $profileId,
              $expectedManagerInstanceId,
              $expectedGeneration,
              $expectedDesiredStateHash,
              'queued',
              $requestedBy,
              $requestedAt,
              $expiresAt);
          """;
      insert.Parameters.AddWithValue("$commandId", commandId.ToString("D"));
      insert.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      insert.Parameters.AddWithValue("$profileId", profile.ProfileId);
      insert.Parameters.AddWithValue(
          "$expectedManagerInstanceId",
          fences.ExpectedManagerInstanceId);
      insert.Parameters.AddWithValue(
          "$expectedGeneration",
          fences.ExpectedGeneration);
      insert.Parameters.AddWithValue(
          "$expectedDesiredStateHash",
          fences.ExpectedDesiredStateHash is null
              ? DBNull.Value
              : fences.ExpectedDesiredStateHash);
      insert.Parameters.AddWithValue(
          "$requestedBy",
          requestedByGitHubUserId);
      insert.Parameters.AddWithValue(
          "$requestedAt",
          requestedAt.ToString("O", CultureInfo.InvariantCulture));
      insert.Parameters.AddWithValue(
          "$expiresAt",
          effectiveExpiry.ToString("O", CultureInfo.InvariantCulture));
      await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    await transaction.CommitAsync(cancellationToken);
    return new RecoveryCommandQueueResult(
        RecoveryCommandQueueStatus.Queued,
        commandId);
  }

  public async Task<RecoverManagerCommand?> ApplyConnectorSyncAsync(
      Guid nodeId,
      RecoveryOperatorCapability? capability,
      RecoveryCommandProgress? progress,
      RecoveryCommandOutcome? outcome,
      DateTimeOffset receivedAt,
      DateTimeOffset redeliverBefore,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);

    await using (var capabilityCommand = connection.CreateCommand())
    {
      capabilityCommand.Transaction = transaction;
      capabilityCommand.CommandText =
          """
          UPDATE nodes
          SET recovery_capability_json = $capability,
              recovery_capability_at = $receivedAt
          WHERE node_id = $nodeId
            AND revoked_at IS NULL;
          """;
      capabilityCommand.Parameters.AddWithValue(
          "$capability",
          capability is null
              ? DBNull.Value
              : JsonSerializer.Serialize(
                  capability,
                  PitCrewProtocolJsonContext.Default.RecoveryOperatorCapability));
      capabilityCommand.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      capabilityCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      if (await capabilityCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        throw new InvalidOperationException(
            $"Node '{nodeId}' was not available for recovery synchronization.");
      }
    }

    if (progress is not null)
    {
      await ApplyProgressAsync(
          connection,
          transaction,
          nodeId,
          progress,
          cancellationToken);
    }
    if (outcome is not null)
    {
      await ApplyOutcomeAsync(
          connection,
          transaction,
          nodeId,
          outcome,
          cancellationToken);
    }
    await ApplyExpiryAsync(
        connection,
        transaction,
        nodeId,
        receivedAt,
        cancellationToken);

    var offered = await OfferAsync(
        connection,
        transaction,
        nodeId,
        capability,
        receivedAt,
        redeliverBefore,
        cancellationToken);
    await SqliteProfileOperationSlot.ReleaseCompletedAsync(
        connection,
        transaction,
        nodeId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return offered;
  }

  public async Task<IReadOnlyList<NodeRecoveryControls>> GetControlsAsync(
      string tenantId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    var capabilities = new Dictionary<Guid, RecoveryOperatorCapability>();
    await using (var capabilityCommand = connection.CreateCommand())
    {
      capabilityCommand.CommandText =
          """
          SELECT node_id, recovery_capability_json
          FROM nodes
          WHERE tenant_id = $tenantId
            AND recovery_capability_json IS NOT NULL;
          """;
      capabilityCommand.Parameters.AddWithValue("$tenantId", tenantId);
      await using var reader = await capabilityCommand.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        var capability = JsonSerializer.Deserialize(
            reader.GetString(1),
            PitCrewProtocolJsonContext.Default.RecoveryOperatorCapability);
        if (capability is not null)
        {
          capabilities[Guid.Parse(
              reader.GetString(0),
              CultureInfo.InvariantCulture)] = capability;
        }
      }
    }

    var commands =
        new Dictionary<(Guid NodeId, string ProfileId), RecoveryCommandState>();
    await using (var commandQuery = connection.CreateCommand())
    {
      commandQuery.CommandText =
          """
          SELECT
              c.node_id,
              c.profile_id,
              c.command_id,
              c.status,
              c.failure_category,
              c.requested_by_github_user_id,
              c.requested_at,
              c.expires_at,
              c.delivered_at,
              c.claimed_at,
              c.started_at,
              c.completed_at,
              c.before_manager_instance_id,
              c.after_manager_instance_id,
              c.result_message
          FROM recovery_commands AS c
          INNER JOIN nodes AS n ON n.node_id = c.node_id
          WHERE n.tenant_id = $tenantId
          ORDER BY c.requested_at DESC;
          """;
      commandQuery.Parameters.AddWithValue("$tenantId", tenantId);
      await using var reader = await commandQuery.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        var nodeId = Guid.Parse(
            reader.GetString(0),
            CultureInfo.InvariantCulture);
        var key = (nodeId, reader.GetString(1));
        if (commands.ContainsKey(key))
        {
          continue;
        }
        commands[key] = new RecoveryCommandState(
            Guid.Parse(
                reader.GetString(2),
                CultureInfo.InvariantCulture),
            reader.GetString(3),
            await ReadStringOrNullAsync(reader, 4, cancellationToken),
            reader.GetString(5),
            ReadTimestamp(reader.GetString(6)),
            ReadTimestamp(reader.GetString(7)),
            await ReadTimestampOrNullAsync(reader, 8, cancellationToken),
            await ReadTimestampOrNullAsync(reader, 9, cancellationToken),
            await ReadTimestampOrNullAsync(reader, 10, cancellationToken),
            await ReadTimestampOrNullAsync(reader, 11, cancellationToken),
            await ReadStringOrNullAsync(reader, 12, cancellationToken),
            await ReadStringOrNullAsync(reader, 13, cancellationToken),
            await ReadStringOrNullAsync(reader, 14, cancellationToken));
      }
    }

    return capabilities
        .OrderBy(pair => pair.Key)
        .Select(pair => new NodeRecoveryControls(
            pair.Key,
            pair.Value.Profiles
                .OrderBy(profile => profile.ProfileId, StringComparer.Ordinal)
                .Select(profile => new RecoveryControlState(
                    profile.ProfileId,
                    profile.ManagerContractVersion,
                    profile.ManagerContractSupported,
                    profile.ExpectedManagerInstanceId,
                    profile.DesiredGeneration,
                    profile.DesiredStateHash,
                    profile.ObservedStateAgeSeconds,
                    profile.RecoveryAllowed,
                    profile.SingleManagerResolved,
                    profile.OperationActive,
                    commands.TryGetValue(
                        (pair.Key, profile.ProfileId),
                        out var command)
                        ? command
                        : null))
                .ToArray()))
        .ToArray();
  }

  private static async Task ApplyProgressAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      RecoveryCommandProgress progress,
      CancellationToken cancellationToken)
  {
    const string claimSql =
        """
        UPDATE recovery_commands
        SET status = 'claimed',
            claimed_at = $reportedAt
        WHERE command_id = $commandId
          AND node_id = $nodeId
          AND status = 'queued';
        """;
    const string startSql =
        """
        UPDATE recovery_commands
        SET status = 'started',
            claimed_at = COALESCE(claimed_at, $reportedAt),
            started_at = $reportedAt
        WHERE command_id = $commandId
          AND node_id = $nodeId
          AND status IN ('queued', 'claimed');
        """;

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = string.Equals(
            progress.Phase,
            "claimed",
            StringComparison.Ordinal)
        ? claimSql
        : startSql;
    command.Parameters.AddWithValue(
        "$reportedAt",
        progress.ReportedAt.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue(
        "$commandId",
        progress.CommandId.ToString("D"));
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task ApplyOutcomeAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      RecoveryCommandOutcome outcome,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE recovery_commands
        SET status = $status,
            failure_category = $failureCategory,
            completed_at = $completedAt,
            claimed_at = COALESCE(claimed_at, $completedAt),
            started_at = COALESCE(
                started_at,
                CASE WHEN $status = 'succeeded' THEN $completedAt END),
            before_manager_instance_id = $beforeManagerInstanceId,
            after_manager_instance_id = $afterManagerInstanceId,
            result_message = $message
        WHERE command_id = $commandId
          AND node_id = $nodeId
          AND status IN ('queued', 'claimed', 'started');
        """;
    command.Parameters.AddWithValue("$status", outcome.Status);
    command.Parameters.AddWithValue(
        "$failureCategory",
        string.Equals(outcome.Status, "succeeded", StringComparison.Ordinal)
            ? DBNull.Value
            : outcome.FailureCategory ?? "unknown");
    command.Parameters.AddWithValue(
        "$completedAt",
        outcome.CompletedAt.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue(
        "$beforeManagerInstanceId",
        outcome.BeforeManagerInstanceId is null
            ? DBNull.Value
            : outcome.BeforeManagerInstanceId);
    command.Parameters.AddWithValue(
        "$afterManagerInstanceId",
        outcome.AfterManagerInstanceId is null
            ? DBNull.Value
            : outcome.AfterManagerInstanceId);
    command.Parameters.AddWithValue(
        "$message",
        outcome.Message is null
            ? DBNull.Value
            : outcome.Message);
    command.Parameters.AddWithValue(
        "$commandId",
        outcome.CommandId.ToString("D"));
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task ApplyExpiryAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE recovery_commands
        SET status = 'expired',
            failure_category = 'expired',
            completed_at = $receivedAt,
            result_message = 'Command expired before execution started.'
        WHERE node_id = $nodeId
          AND status IN ('queued', 'claimed')
          AND expires_at <= $receivedAt;

        UPDATE recovery_commands
        SET status = 'indeterminate',
            failure_category = 'timeout',
            completed_at = $receivedAt,
            result_message = 'Execution started but no terminal outcome was reported.'
        WHERE node_id = $nodeId
          AND status = 'started'
          AND expires_at <= $receivedAt;
        """;
    command.Parameters.AddWithValue(
        "$receivedAt",
        receivedAt.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<RecoverManagerCommand?> OfferAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      RecoveryOperatorCapability? capability,
      DateTimeOffset receivedAt,
      DateTimeOffset redeliverBefore,
      CancellationToken cancellationToken)
  {
    QueuedCommand? queued = null;
    await using (var select = connection.CreateCommand())
    {
      select.Transaction = transaction;
      select.CommandText =
          """
          SELECT
              command_id,
              profile_id,
              expected_manager_instance_id,
              expected_generation,
              expected_desired_state_hash,
              requested_at,
              expires_at
          FROM recovery_commands
          WHERE node_id = $nodeId
            AND status = 'queued'
            AND expires_at > $receivedAt
            AND (delivered_at IS NULL OR delivered_at <= $redeliverBefore)
          ORDER BY requested_at
          LIMIT 1;
          """;
      select.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      select.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      select.Parameters.AddWithValue(
          "$redeliverBefore",
          redeliverBefore.ToString("O", CultureInfo.InvariantCulture));
      await using var reader = await select.ExecuteReaderAsync(
          cancellationToken);
      if (await reader.ReadAsync(cancellationToken))
      {
        queued = new QueuedCommand(
            Guid.Parse(
                reader.GetString(0),
                CultureInfo.InvariantCulture),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            await ReadStringOrNullAsync(reader, 4, cancellationToken),
            ReadTimestamp(reader.GetString(5)),
            ReadTimestamp(reader.GetString(6)));
      }
    }
    if (queued is null)
    {
      return null;
    }

    var profile = capability?.Profiles.FirstOrDefault(candidate =>
        string.Equals(
            candidate.ProfileId,
            queued.ProfileId,
            StringComparison.OrdinalIgnoreCase));
    var rejection = profile switch
    {
      null => ("not-allowed", "Connector no longer advertises recovery for this profile."),
      _ when !profile.RecoveryAllowed || !profile.ManagerContractSupported =>
          ("not-allowed", "Local policy no longer allows recovery for this profile."),
      _ when !profile.SingleManagerResolved =>
          ("manager-unresolved", "Exactly one running manager is no longer locally resolvable."),
      _ when !string.Equals(
              profile.ExpectedManagerInstanceId,
              queued.ExpectedManagerInstanceId,
              StringComparison.Ordinal) ||
          profile.DesiredGeneration != queued.ExpectedGeneration ||
          !string.Equals(
              profile.DesiredStateHash,
              queued.ExpectedDesiredStateHash,
              StringComparison.OrdinalIgnoreCase) =>
          ("stale-fence", "Profile fences changed before command delivery."),
      _ => default((string, string)?),
    };
    if (rejection is not null)
    {
      await using var reject = connection.CreateCommand();
      reject.Transaction = transaction;
      reject.CommandText =
          """
          UPDATE recovery_commands
          SET status = 'rejected',
              failure_category = $failureCategory,
              completed_at = $receivedAt,
              result_message = $message
          WHERE command_id = $commandId
            AND status = 'queued';
          """;
      reject.Parameters.AddWithValue(
          "$failureCategory",
          rejection.Value.Item1);
      reject.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      reject.Parameters.AddWithValue("$message", rejection.Value.Item2);
      reject.Parameters.AddWithValue(
          "$commandId",
          queued.CommandId.ToString("D"));
      await reject.ExecuteNonQueryAsync(cancellationToken);
      return null;
    }

    await using (var deliver = connection.CreateCommand())
    {
      deliver.Transaction = transaction;
      deliver.CommandText =
          """
          UPDATE recovery_commands
          SET delivered_at = $receivedAt,
              delivery_attempts = delivery_attempts + 1
          WHERE command_id = $commandId
            AND status = 'queued';
          """;
      deliver.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      deliver.Parameters.AddWithValue(
          "$commandId",
          queued.CommandId.ToString("D"));
      await deliver.ExecuteNonQueryAsync(cancellationToken);
    }

    return new RecoverManagerCommand(
        queued.CommandId,
        queued.ProfileId,
        queued.ExpectedManagerInstanceId,
        queued.ExpectedGeneration,
        queued.ExpectedDesiredStateHash,
        queued.RequestedAt,
        queued.ExpiresAt);
  }

  private static DateTimeOffset ReadTimestamp(string value) =>
      DateTimeOffset.Parse(
          value,
          CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind);

  private static async Task<string?> ReadStringOrNullAsync(
      SqliteDataReader reader,
      int ordinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(ordinal, cancellationToken)
          ? null
          : reader.GetString(ordinal);

  private static async Task<DateTimeOffset?> ReadTimestampOrNullAsync(
      SqliteDataReader reader,
      int ordinal,
      CancellationToken cancellationToken) =>
      await reader.IsDBNullAsync(ordinal, cancellationToken)
          ? null
          : ReadTimestamp(reader.GetString(ordinal));

  private sealed record QueuedCommand(
      Guid CommandId,
      string ProfileId,
      string ExpectedManagerInstanceId,
      int ExpectedGeneration,
      string? ExpectedDesiredStateHash,
      DateTimeOffset RequestedAt,
      DateTimeOffset ExpiresAt);
}
