using System.Globalization;
using System.Text.Json;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteCapacityCommandStore(
    SqliteConnectionFactory _connectionFactory) : ICapacityCommandStore
{
  public async Task<CapacityCommandQueueResult> QueueAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      int maximum,
      string requestedByGitHubUserId,
      DateTimeOffset requestedAt,
      DateTimeOffset expiresAt,
      CancellationToken cancellationToken,
      Guid? resumeCommandId = null)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);

    CapacityOperatorCapability? capability;
    await using (var capabilityCommand = connection.CreateCommand())
    {
      capabilityCommand.Transaction = transaction;
      capabilityCommand.CommandText =
          """
          SELECT capacity_capability_json
          FROM nodes
          WHERE tenant_id = $tenantId
            AND node_id = $nodeId
            AND revoked_at IS NULL;
          """;
      capabilityCommand.Parameters.AddWithValue("$tenantId", tenantId);
      capabilityCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      var value = await capabilityCommand.ExecuteScalarAsync(
          cancellationToken);
      if (value is null)
      {
        return new CapacityCommandQueueResult(
            CapacityCommandQueueStatus.NodeNotFound,
            null);
      }
      capability = value is DBNull
          ? null
          : JsonSerializer.Deserialize(
              (string)value,
              PitCrewProtocolJsonContext.Default.CapacityOperatorCapability);
    }

    var profile = capability?.Profiles.FirstOrDefault(candidate =>
        string.Equals(
            candidate.ProfileId,
            profileId,
            StringComparison.OrdinalIgnoreCase));
    if (profile is null)
    {
      return new CapacityCommandQueueResult(
          CapacityCommandQueueStatus.Unsupported,
          null);
    }
    if (maximum < 0 ||
        (maximum == 0 && !profile.SupportsZeroMaximum) ||
        maximum > profile.MaximumAllowed ||
        maximum == profile.CurrentMaximum)
    {
      return new CapacityCommandQueueResult(
          CapacityCommandQueueStatus.InvalidMaximum,
          null);
    }

    Guid? effectiveResumeCommandId = null;
    if (profile.CurrentMaximum == 0 && maximum > 0)
    {
      effectiveResumeCommandId = await FindResumeCommandIdAsync(
          connection,
          transaction,
          nodeId,
          profile,
          maximum,
          resumeCommandId,
          cancellationToken);
      if (resumeCommandId is not null &&
          effectiveResumeCommandId is null)
      {
        return new CapacityCommandQueueResult(
            CapacityCommandQueueStatus.StaleResume,
            null);
      }
    }
    else if (resumeCommandId is not null)
    {
      return new CapacityCommandQueueResult(
          CapacityCommandQueueStatus.StaleResume,
          null);
    }

    var commandId = Guid.NewGuid();
    if (!await SqliteProfileOperationSlot.AcquireAsync(
        connection,
        transaction,
        nodeId,
        profile.ProfileId,
        SqliteProfileOperationSlot.CapacityKind,
        commandId,
        requestedAt,
        cancellationToken))
    {
      return new CapacityCommandQueueResult(
          CapacityCommandQueueStatus.Conflict,
          null);
    }

    await using (var insert = connection.CreateCommand())
    {
      insert.Transaction = transaction;
      insert.CommandText =
          """
          INSERT INTO capacity_commands (
              command_id,
              node_id,
              profile_id,
              expected_generation,
              previous_maximum,
              requested_maximum,
              resumes_command_id,
              maximum_allowed_at_request,
              status,
              requested_by_github_user_id,
              requested_at,
              expires_at)
          VALUES (
              $commandId,
              $nodeId,
              $profileId,
              $expectedGeneration,
              $previousMaximum,
              $requestedMaximum,
              $resumesCommandId,
              $maximumAllowed,
              'pending',
              $requestedBy,
              $requestedAt,
              $expiresAt);
          """;
      insert.Parameters.AddWithValue(
          "$commandId",
          commandId.ToString("D"));
      insert.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
      insert.Parameters.AddWithValue("$profileId", profile.ProfileId);
      insert.Parameters.AddWithValue(
          "$expectedGeneration",
          profile.Generation);
      insert.Parameters.AddWithValue(
          "$previousMaximum",
          profile.CurrentMaximum);
      insert.Parameters.AddWithValue("$requestedMaximum", maximum);
      insert.Parameters.AddWithValue(
          "$resumesCommandId",
          effectiveResumeCommandId is null
              ? DBNull.Value
              : effectiveResumeCommandId.Value.ToString("D"));
      insert.Parameters.AddWithValue(
          "$maximumAllowed",
          profile.MaximumAllowed);
      insert.Parameters.AddWithValue(
          "$requestedBy",
          requestedByGitHubUserId);
      insert.Parameters.AddWithValue(
          "$requestedAt",
          requestedAt.ToString("O", CultureInfo.InvariantCulture));
      insert.Parameters.AddWithValue(
          "$expiresAt",
          expiresAt.ToString("O", CultureInfo.InvariantCulture));
      await insert.ExecuteNonQueryAsync(cancellationToken);
    }
    await transaction.CommitAsync(cancellationToken);
    return new CapacityCommandQueueResult(
        CapacityCommandQueueStatus.Queued,
        commandId);
  }

  public async Task<SetCapacityCommand?> ApplyConnectorSyncAsync(
      Guid nodeId,
      CapacityOperatorCapability? capability,
      CapacityCommandOutcome? outcome,
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
          SET capacity_capability_json = $capability,
              capacity_capability_at = $receivedAt
          WHERE node_id = $nodeId
            AND revoked_at IS NULL;
          """;
      capabilityCommand.Parameters.AddWithValue(
          "$capability",
          capability is null
              ? DBNull.Value
              : JsonSerializer.Serialize(
                  capability,
                  PitCrewProtocolJsonContext.Default.CapacityOperatorCapability));
      capabilityCommand.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      capabilityCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      if (await capabilityCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
      {
        throw new InvalidOperationException(
            $"Node '{nodeId}' was not available for capacity synchronization.");
      }
    }

    if (outcome is not null)
    {
      await using var completion = connection.CreateCommand();
      completion.Transaction = transaction;
      completion.CommandText =
          """
          UPDATE capacity_commands
          SET status = $status,
              completed_at = $completedAt,
              accepted_generation = $acceptedGeneration,
              result_message = $message
          WHERE command_id = $commandId
            AND node_id = $nodeId
            AND status = 'delivered';
          """;
      completion.Parameters.AddWithValue("$status", outcome.Status);
      completion.Parameters.AddWithValue(
          "$completedAt",
          outcome.CompletedAt.ToString(
              "O",
              CultureInfo.InvariantCulture));
      completion.Parameters.AddWithValue(
          "$acceptedGeneration",
          outcome.AcceptedGeneration is null
              ? DBNull.Value
              : outcome.AcceptedGeneration.Value);
      completion.Parameters.AddWithValue(
          "$message",
          outcome.Message is null
              ? DBNull.Value
              : outcome.Message);
      completion.Parameters.AddWithValue(
          "$commandId",
          outcome.CommandId.ToString("D"));
      completion.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      await completion.ExecuteNonQueryAsync(cancellationToken);
    }

    await using (var expire = connection.CreateCommand())
    {
      expire.Transaction = transaction;
      expire.CommandText =
          """
          UPDATE capacity_commands
          SET status = 'rejected',
              completed_at = $receivedAt,
              result_message = 'Command expired before execution.'
          WHERE node_id = $nodeId
            AND status IN ('pending', 'delivered')
            AND expires_at <= $receivedAt;
          """;
      expire.Parameters.AddWithValue(
          "$receivedAt",
          receivedAt.ToString("O", CultureInfo.InvariantCulture));
      expire.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      await expire.ExecuteNonQueryAsync(cancellationToken);
    }

    PendingCommand? pending = null;
    await using (var select = connection.CreateCommand())
    {
      select.Transaction = transaction;
      select.CommandText =
          """
          SELECT
              command_id,
              profile_id,
              expected_generation,
              requested_maximum,
              expires_at,
              status
          FROM capacity_commands
          WHERE node_id = $nodeId
            AND (
                status = 'pending'
                OR (
                    status = 'delivered'
                    AND delivered_at <= $redeliverBefore))
          ORDER BY requested_at
          LIMIT 1;
          """;
      select.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      select.Parameters.AddWithValue(
          "$redeliverBefore",
          redeliverBefore.ToString("O", CultureInfo.InvariantCulture));
      await using var reader = await select.ExecuteReaderAsync(
          cancellationToken);
      if (await reader.ReadAsync(cancellationToken))
      {
        pending = new PendingCommand(
            Guid.Parse(
                reader.GetString(0),
                CultureInfo.InvariantCulture),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            DateTimeOffset.Parse(
                reader.GetString(4),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            reader.GetString(5));
      }
    }

    SetCapacityCommand? claimed = null;
    if (pending is not null)
    {
      var profile = capability?.Profiles.FirstOrDefault(candidate =>
          string.Equals(
              candidate.ProfileId,
              pending.ProfileId,
              StringComparison.OrdinalIgnoreCase));
      var inferredSuccess =
          string.Equals(
              pending.Status,
              "delivered",
              StringComparison.Ordinal) &&
          profile is not null &&
          profile.Generation > pending.ExpectedGeneration &&
          (pending.Maximum != 0 ||
              (pending.ExpectedGeneration < int.MaxValue &&
                  profile.Generation == pending.ExpectedGeneration + 1)) &&
          profile.CurrentMaximum == pending.Maximum;
      if (inferredSuccess)
      {
        await using var succeed = connection.CreateCommand();
        succeed.Transaction = transaction;
        succeed.CommandText =
            """
            UPDATE capacity_commands
            SET status = 'succeeded',
                completed_at = $receivedAt,
                accepted_generation = $acceptedGeneration,
                result_message = 'Capacity convergence was observed after connector restart.'
            WHERE command_id = $commandId;
            """;
        succeed.Parameters.AddWithValue(
            "$receivedAt",
            receivedAt.ToString("O", CultureInfo.InvariantCulture));
        succeed.Parameters.AddWithValue(
            "$acceptedGeneration",
            profile!.Generation);
        succeed.Parameters.AddWithValue(
            "$commandId",
            pending.CommandId.ToString("D"));
        await succeed.ExecuteNonQueryAsync(cancellationToken);
      }
      else
      {
        var rejection = profile switch
        {
          null => "Connector no longer advertises this profile.",
          _ when profile.Generation != pending.ExpectedGeneration =>
              "Profile generation changed before command delivery.",
          _ when pending.Maximum == 0 &&
              !profile.SupportsZeroMaximum =>
              "Connector no longer advertises zero-capacity support.",
          _ when pending.Maximum > profile.MaximumAllowed =>
              "Connector capacity ceiling changed before command delivery.",
          _ => null,
        };
        if (rejection is not null)
        {
          await using var reject = connection.CreateCommand();
          reject.Transaction = transaction;
          reject.CommandText =
              """
              UPDATE capacity_commands
              SET status = 'rejected',
                  completed_at = $receivedAt,
                  result_message = $message
              WHERE command_id = $commandId;
              """;
          reject.Parameters.AddWithValue(
              "$receivedAt",
              receivedAt.ToString("O", CultureInfo.InvariantCulture));
          reject.Parameters.AddWithValue("$message", rejection);
          reject.Parameters.AddWithValue(
              "$commandId",
              pending.CommandId.ToString("D"));
          await reject.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
          await using var deliver = connection.CreateCommand();
          deliver.Transaction = transaction;
          deliver.CommandText =
              """
              UPDATE capacity_commands
              SET status = 'delivered',
                  delivered_at = $receivedAt,
                  delivery_attempts = delivery_attempts + 1
              WHERE command_id = $commandId;
              """;
          deliver.Parameters.AddWithValue(
              "$receivedAt",
              receivedAt.ToString("O", CultureInfo.InvariantCulture));
          deliver.Parameters.AddWithValue(
              "$commandId",
              pending.CommandId.ToString("D"));
          await deliver.ExecuteNonQueryAsync(cancellationToken);
          claimed = new SetCapacityCommand(
              pending.CommandId,
              pending.ProfileId,
              pending.ExpectedGeneration,
              pending.Maximum,
              pending.ExpiresAt);
        }
      }
    }

    await SqliteProfileOperationSlot.ReleaseCompletedAsync(
        connection,
        transaction,
        nodeId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return claimed;
  }

  public async Task<IReadOnlyList<NodeCapacityControls>> GetControlsAsync(
      string tenantId,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    var capabilities = new Dictionary<Guid, CapacityOperatorCapability>();
    await using (var capabilityCommand = connection.CreateCommand())
    {
      capabilityCommand.CommandText =
          """
          SELECT node_id, capacity_capability_json
          FROM nodes
          WHERE tenant_id = $tenantId
            AND capacity_capability_json IS NOT NULL;
          """;
      capabilityCommand.Parameters.AddWithValue("$tenantId", tenantId);
      await using var reader = await capabilityCommand.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        var capability = JsonSerializer.Deserialize(
            reader.GetString(1),
            PitCrewProtocolJsonContext.Default.CapacityOperatorCapability);
        if (capability is not null)
        {
          capabilities[Guid.Parse(
              reader.GetString(0),
              CultureInfo.InvariantCulture)] = capability;
        }
      }
    }

    var commands = new Dictionary<(Guid NodeId, string ProfileId), CapacityCommandState>();
    var pauses = new Dictionary<(Guid NodeId, string ProfileId), ResumablePause>();
    await using (var commandQuery = connection.CreateCommand())
    {
      commandQuery.CommandText =
          """
          SELECT
              c.node_id,
              c.profile_id,
              c.command_id,
              c.previous_maximum,
              c.requested_maximum,
              c.resumes_command_id,
              c.status,
              c.requested_at,
              c.delivered_at,
              c.completed_at,
              c.result_message,
              c.accepted_generation
          FROM capacity_commands AS c
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
        var profileId = reader.GetString(1);
        var key = (nodeId, profileId);
        if (!commands.ContainsKey(key))
        {
          commands[key] = new CapacityCommandState(
              Guid.Parse(
                  reader.GetString(2),
                  CultureInfo.InvariantCulture),
              await reader.IsDBNullAsync(3, cancellationToken)
                  ? null
                  : reader.GetInt32(3),
              reader.GetInt32(4),
              await reader.IsDBNullAsync(5, cancellationToken)
                  ? null
                  : Guid.Parse(
                      reader.GetString(5),
                      CultureInfo.InvariantCulture),
              reader.GetString(6),
              DateTimeOffset.Parse(
                  reader.GetString(7),
                  CultureInfo.InvariantCulture,
                  DateTimeStyles.RoundtripKind),
              await reader.IsDBNullAsync(8, cancellationToken)
                  ? null
                  : DateTimeOffset.Parse(
                      reader.GetString(8),
                      CultureInfo.InvariantCulture,
                      DateTimeStyles.RoundtripKind),
              await reader.IsDBNullAsync(9, cancellationToken)
                  ? null
                  : DateTimeOffset.Parse(
                      reader.GetString(9),
                      CultureInfo.InvariantCulture,
                      DateTimeStyles.RoundtripKind),
              await reader.IsDBNullAsync(10, cancellationToken)
                  ? null
                  : reader.GetString(10));
        }
        if (!pauses.ContainsKey(key) &&
            reader.GetInt32(4) == 0 &&
            string.Equals(
                reader.GetString(6),
                "succeeded",
                StringComparison.Ordinal) &&
            !await reader.IsDBNullAsync(11, cancellationToken))
        {
          if (await reader.IsDBNullAsync(3, cancellationToken))
          {
            continue;
          }
          pauses[key] = new ResumablePause(
              Guid.Parse(
                  reader.GetString(2),
                  CultureInfo.InvariantCulture),
              reader.GetInt32(3),
              reader.GetInt32(11));
        }
      }
    }

    return capabilities
        .OrderBy(pair => pair.Key)
        .Select(pair => new NodeCapacityControls(
            pair.Key,
            pair.Value.Profiles
                .OrderBy(profile => profile.ProfileId)
                .Select(profile => new CapacityControlState(
                    profile.ProfileId,
                    profile.Generation,
                    profile.CurrentMaximum,
                    profile.MaximumAllowed,
                    profile.SupportsZeroMaximum,
                    commands.TryGetValue(
                        (pair.Key, profile.ProfileId),
                        out var command)
                        ? command
                        : null,
                    profile.CurrentMaximum == 0 &&
                        pauses.TryGetValue(
                            (pair.Key, profile.ProfileId),
                            out var pause) &&
                        pause.AcceptedGeneration == profile.Generation
                            ? pause.CommandId
                            : null,
                    profile.CurrentMaximum == 0 &&
                        pauses.TryGetValue(
                            (pair.Key, profile.ProfileId),
                            out pause) &&
                        pause.AcceptedGeneration == profile.Generation
                            ? pause.PreviousMaximum
                            : null))
                .ToArray()))
        .ToArray();
  }

  private sealed record PendingCommand(
      Guid CommandId,
      string ProfileId,
      int ExpectedGeneration,
      int Maximum,
      DateTimeOffset ExpiresAt,
      string Status);

  private sealed record ResumablePause(
      Guid CommandId,
      int PreviousMaximum,
      int AcceptedGeneration);

  private static async Task<Guid?> FindResumeCommandIdAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      CapacityOperatorProfile profile,
      int maximum,
      Guid? pauseCommandId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT command_id
        FROM capacity_commands
        WHERE node_id = $nodeId
          AND profile_id = $profileId
          AND requested_maximum = 0
          AND status = 'succeeded'
          AND previous_maximum = $maximum
          AND accepted_generation = $generation
          AND ($commandId IS NULL OR command_id = $commandId)
        ORDER BY requested_at DESC
        LIMIT 1;
        """;
    command.Parameters.AddWithValue(
        "$commandId",
        pauseCommandId is null
            ? DBNull.Value
            : pauseCommandId.Value.ToString("D"));
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profile.ProfileId);
    command.Parameters.AddWithValue("$maximum", maximum);
    command.Parameters.AddWithValue("$generation", profile.Generation);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    return await reader.ReadAsync(cancellationToken)
        ? Guid.Parse(
            reader.GetString(0),
            CultureInfo.InvariantCulture)
        : null;
  }
}
