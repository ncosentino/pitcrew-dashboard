using System.Globalization;
using System.Text;

using Microsoft.Data.Sqlite;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed class SqliteConnectorHealthStore(
    SqliteConnectionFactory _connectionFactory) : IConnectorHealthStore
{
  public async Task ApplyAsync(
      IFleetStorageTransaction transaction,
      Guid nodeId,
      ConnectorHealthReplay replay,
      DateTimeOffset receivedAt,
      ConnectorHealthRetentionPolicy retention,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(replay);
    ArgumentNullException.ThrowIfNull(retention);
    var enlisted = SqliteFleetTransaction.Resolve(transaction);
    await using var command = enlisted.Connection.CreateCommand();
    command.Transaction = enlisted.Transaction;
    var sql = new StringBuilder(
        """
        INSERT INTO connector_health_current (
            node_id,
            state,
            process_started_at,
            updated_at,
            last_attempt_at,
            last_success_at,
            active_outage_id,
            active_outage_started_at,
            last_failure_at,
            last_failure_category,
            last_failure_profile_id,
            last_failure_detail,
            consecutive_failures,
            next_retry_at,
            last_recovered_outage_id,
            last_recovered_outage_started_at,
            last_recovered_at,
            last_recovered_failure_category,
            received_at)
        VALUES (
            $nodeId,
            $state,
            $processStartedAt,
            $updatedAt,
            $lastAttemptAt,
            $lastSuccessAt,
            $activeOutageId,
            $activeOutageStartedAt,
            $lastFailureAt,
            $lastFailureCategory,
            $lastFailureProfileId,
            $lastFailureDetail,
            $consecutiveFailures,
            $nextRetryAt,
            $lastRecoveredOutageId,
            $lastRecoveredOutageStartedAt,
            $lastRecoveredAt,
            $lastRecoveredFailureCategory,
            $receivedAt)
        ON CONFLICT (node_id) DO UPDATE SET
            state = excluded.state,
            process_started_at = excluded.process_started_at,
            updated_at = excluded.updated_at,
            last_attempt_at = excluded.last_attempt_at,
            last_success_at = excluded.last_success_at,
            active_outage_id = excluded.active_outage_id,
            active_outage_started_at = excluded.active_outage_started_at,
            last_failure_at = excluded.last_failure_at,
            last_failure_category = excluded.last_failure_category,
            last_failure_profile_id = excluded.last_failure_profile_id,
            last_failure_detail = excluded.last_failure_detail,
            consecutive_failures = excluded.consecutive_failures,
            next_retry_at = excluded.next_retry_at,
            last_recovered_outage_id =
                excluded.last_recovered_outage_id,
            last_recovered_outage_started_at =
                excluded.last_recovered_outage_started_at,
            last_recovered_at = excluded.last_recovered_at,
            last_recovered_failure_category =
                excluded.last_recovered_failure_category,
            received_at = excluded.received_at
        WHERE excluded.received_at >=
            connector_health_current.received_at;
        """);
    AddParameter(
        command,
        "$nodeId",
        nodeId.ToString("D"));
    AddSnapshotParameters(
        command,
        replay.Snapshot,
        receivedAt);

    if (replay.Events.Count > 0)
    {
      sql.AppendLine(
          """

          INSERT INTO connector_health_events (
              node_id,
              event_id,
              kind,
              occurred_at,
              state,
              outage_id,
              outage_started_at,
              failure_category,
              profile_id,
              consecutive_failures,
              retry_delay_seconds,
              detail,
              received_at)
          VALUES
          """);
      for (var index = 0; index < replay.Events.Count; index++)
      {
        if (index > 0)
        {
          sql.AppendLine(",");
        }
        sql.Append(
            CultureInfo.InvariantCulture,
            $"""
                ($nodeId,
                 $eventId{index},
                 $eventKind{index},
                 $eventOccurredAt{index},
                 $eventState{index},
                 $eventOutageId{index},
                 $eventOutageStartedAt{index},
                 $eventFailureCategory{index},
                 $eventProfileId{index},
                 $eventConsecutiveFailures{index},
                 $eventRetryDelaySeconds{index},
                 $eventDetail{index},
                 $receivedAt)
            """);
        AddEventParameters(
            command,
            replay.Events[index],
            index);
      }
      sql.AppendLine(
          """

          ON CONFLICT (node_id, event_id) DO NOTHING;
          """);
    }

    sql.AppendLine(
        """

        DELETE FROM connector_health_events
        WHERE node_id = $nodeId
          AND received_at < $oldestReceivedAt;

        DELETE FROM connector_health_events
        WHERE node_id = $nodeId
          AND event_id IN (
              SELECT event_id
              FROM connector_health_events
              WHERE node_id = $nodeId
              ORDER BY occurred_at DESC, event_id DESC
              LIMIT -1 OFFSET $maximumEvents);
        """);
    AddParameter(
        command,
        "$oldestReceivedAt",
        receivedAt
            .Subtract(retention.MaximumAge)
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture));
    AddParameter(
        command,
        "$maximumEvents",
        retention.MaximumEventsPerNode);
    command.CommandText = sql.ToString();
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  public async Task<ConnectorHealthProjection?> GetAsync(
      string tenantId,
      Guid nodeId,
      int maximumEvents,
      CancellationToken cancellationToken)
  {
    if (maximumEvents is < 1 or > 1_000)
    {
      throw new ArgumentOutOfRangeException(
          nameof(maximumEvents));
    }
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            health.state,
            health.process_started_at,
            health.updated_at,
            health.last_attempt_at,
            health.last_success_at,
            health.active_outage_id,
            health.active_outage_started_at,
            health.last_failure_at,
            health.last_failure_category,
            health.last_failure_profile_id,
            health.last_failure_detail,
            health.consecutive_failures,
            health.next_retry_at,
            health.last_recovered_outage_id,
            health.last_recovered_outage_started_at,
            health.last_recovered_at,
            health.last_recovered_failure_category,
            health.received_at
        FROM connector_health_current AS health
        INNER JOIN nodes
          ON nodes.node_id = health.node_id
        WHERE health.node_id = $nodeId
          AND nodes.tenant_id = $tenantId;

        SELECT
            events.event_id,
            events.kind,
            events.occurred_at,
            events.state,
            events.outage_id,
            events.outage_started_at,
            events.failure_category,
            events.profile_id,
            events.consecutive_failures,
            events.retry_delay_seconds,
            events.detail
        FROM connector_health_events AS events
        INNER JOIN nodes
          ON nodes.node_id = events.node_id
        WHERE events.node_id = $nodeId
          AND nodes.tenant_id = $tenantId
        ORDER BY events.occurred_at DESC, events.event_id DESC
        LIMIT $eventLimit;
        """;
    AddParameter(
        command,
        "$nodeId",
        nodeId.ToString("D"));
    AddParameter(
        command,
        "$tenantId",
        tenantId);
    AddParameter(
        command,
        "$eventLimit",
        maximumEvents + 1);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return null;
    }
    var current = new SqliteRowReader(reader);
    var snapshot = new ConnectorHealthReplaySnapshot(
        current.String("state"),
        current.Time("process_started_at"),
        current.Time("updated_at"),
        current.OptionalTime("last_attempt_at"),
        current.OptionalTime("last_success_at"),
        ParseOptionalGuid(
            current.OptionalString("active_outage_id")),
        current.OptionalTime("active_outage_started_at"),
        current.OptionalTime("last_failure_at"),
        current.OptionalString("last_failure_category"),
        current.OptionalString("last_failure_profile_id"),
        current.OptionalString("last_failure_detail"),
        current.Int32("consecutive_failures"),
        current.OptionalTime("next_retry_at"),
        ParseOptionalGuid(
            current.OptionalString("last_recovered_outage_id")),
        current.OptionalTime(
            "last_recovered_outage_started_at"),
        current.OptionalTime("last_recovered_at"),
        current.OptionalString(
            "last_recovered_failure_category"));
    var receivedAt = current.Time("received_at");
    await reader.NextResultAsync(cancellationToken);
    var events = new List<ConnectorHealthReplayEvent>(
        maximumEvents + 1);
    while (await reader.ReadAsync(cancellationToken))
    {
      var row = new SqliteRowReader(reader);
      events.Add(
          new ConnectorHealthReplayEvent(
              Guid.Parse(
                  row.String("event_id"),
                  CultureInfo.InvariantCulture),
              row.String("kind"),
              row.Time("occurred_at"),
              row.String("state"),
              ParseOptionalGuid(
                  row.OptionalString("outage_id")),
              row.OptionalTime("outage_started_at"),
              row.OptionalString("failure_category"),
              row.OptionalString("profile_id"),
              row.Int32("consecutive_failures"),
              row.OptionalInt32("retry_delay_seconds"),
              row.OptionalString("detail")));
    }
    var truncated = events.Count > maximumEvents;
    if (truncated)
    {
      events.RemoveAt(events.Count - 1);
    }
    return new ConnectorHealthProjection(
        snapshot,
        receivedAt,
        events,
        truncated);
  }

  private static void AddSnapshotParameters(
      SqliteCommand command,
      ConnectorHealthReplaySnapshot snapshot,
      DateTimeOffset receivedAt)
  {
    AddParameter(command, "$state", snapshot.State);
    AddTime(
        command,
        "$processStartedAt",
        snapshot.ProcessStartedAt);
    AddTime(command, "$updatedAt", snapshot.UpdatedAt);
    AddOptionalTime(
        command,
        "$lastAttemptAt",
        snapshot.LastAttemptAt);
    AddOptionalTime(
        command,
        "$lastSuccessAt",
        snapshot.LastSuccessAt);
    AddOptionalGuid(
        command,
        "$activeOutageId",
        snapshot.ActiveOutageId);
    AddOptionalTime(
        command,
        "$activeOutageStartedAt",
        snapshot.ActiveOutageStartedAt);
    AddOptionalTime(
        command,
        "$lastFailureAt",
        snapshot.LastFailureAt);
    AddParameter(
        command,
        "$lastFailureCategory",
        snapshot.LastFailureCategory);
    AddParameter(
        command,
        "$lastFailureProfileId",
        snapshot.LastFailureProfileId);
    AddParameter(
        command,
        "$lastFailureDetail",
        snapshot.LastFailureDetail);
    AddParameter(
        command,
        "$consecutiveFailures",
        snapshot.ConsecutiveFailures);
    AddOptionalTime(
        command,
        "$nextRetryAt",
        snapshot.NextRetryAt);
    AddOptionalGuid(
        command,
        "$lastRecoveredOutageId",
        snapshot.LastRecoveredOutageId);
    AddOptionalTime(
        command,
        "$lastRecoveredOutageStartedAt",
        snapshot.LastRecoveredOutageStartedAt);
    AddOptionalTime(
        command,
        "$lastRecoveredAt",
        snapshot.LastRecoveredAt);
    AddParameter(
        command,
        "$lastRecoveredFailureCategory",
        snapshot.LastRecoveredFailureCategory);
    AddTime(command, "$receivedAt", receivedAt);
  }

  private static void AddEventParameters(
      SqliteCommand command,
      ConnectorHealthReplayEvent entry,
      int index)
  {
    AddParameter(
        command,
        $"$eventId{index}",
        entry.EventId.ToString("D"));
    AddParameter(
        command,
        $"$eventKind{index}",
        entry.Kind);
    AddTime(
        command,
        $"$eventOccurredAt{index}",
        entry.OccurredAt);
    AddParameter(
        command,
        $"$eventState{index}",
        entry.State);
    AddOptionalGuid(
        command,
        $"$eventOutageId{index}",
        entry.OutageId);
    AddOptionalTime(
        command,
        $"$eventOutageStartedAt{index}",
        entry.OutageStartedAt);
    AddParameter(
        command,
        $"$eventFailureCategory{index}",
        entry.FailureCategory);
    AddParameter(
        command,
        $"$eventProfileId{index}",
        entry.ProfileId);
    AddParameter(
        command,
        $"$eventConsecutiveFailures{index}",
        entry.ConsecutiveFailures);
    AddParameter(
        command,
        $"$eventRetryDelaySeconds{index}",
        entry.RetryDelaySeconds);
    AddParameter(
        command,
        $"$eventDetail{index}",
        entry.Detail);
  }

  private static void AddTime(
      SqliteCommand command,
      string name,
      DateTimeOffset value) =>
      AddParameter(
          command,
          name,
          value
              .ToUniversalTime()
              .ToString("O", CultureInfo.InvariantCulture));

  private static void AddOptionalTime(
      SqliteCommand command,
      string name,
      DateTimeOffset? value) =>
      AddParameter(
          command,
          name,
          value?
              .ToUniversalTime()
              .ToString("O", CultureInfo.InvariantCulture));

  private static void AddOptionalGuid(
      SqliteCommand command,
      string name,
      Guid? value) =>
      AddParameter(
          command,
          name,
          value?.ToString("D"));

  private static void AddParameter(
      SqliteCommand command,
      string name,
      object? value) =>
      command.Parameters.AddWithValue(
          name,
          value ?? DBNull.Value);

  private static Guid? ParseOptionalGuid(string? value) =>
      value is null
          ? null
          : Guid.Parse(
              value,
              CultureInfo.InvariantCulture);
}
