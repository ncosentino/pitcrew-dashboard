using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

/// <summary>
/// Bounds retained history deterministically for one node and, on a throttled schedule, globally.
/// </summary>
/// <remarks>
/// Every ceiling is enforced by ranking full primary keys with <c>ROW_NUMBER</c> instead of by
/// deleting below a timestamp cutoff, so rows sharing a timestamp or an hourly bucket keep exactly
/// the configured newest count rather than all of them or none of them. Retention never depends on
/// the node that happens to be syncing: a bounded global sweep ages history across abandoned nodes
/// and enforces database-wide, node-count, and profile-history ceilings inside the same
/// transaction, and every eviction leaves either an updated cursor or a tombstone so completeness
/// provenance survives the deletion.
/// </remarks>
internal sealed partial class SqliteFleetHistoryStore
{
  private const string SubsystemHealthTable = "profile_subsystem_health";
  private const string CapacityDeficitTable = "profile_capacity_deficits";

  private static readonly string[] ProfileScopedTables =
  [
      "profile_telemetry_samples",
      "profile_telemetry_rollups",
      "profile_manager_events",
      "profile_event_identities",
      SubsystemHealthTable,
      CapacityDeficitTable,
  ];

  private static readonly HistoryTable[] CountedTables =
  [
      new("profile_telemetry_samples", "observed_at", "observed_at"),
      new("profile_telemetry_rollups", "bucket_start", "bucket_start"),
      new("profile_manager_events", "observed_at", "epoch, sequence"),
      new(SubsystemHealthTable, "observed_at", "subsystem, observed_at"),
      new(CapacityDeficitTable, "observed_at", "target_key, observed_at"),
  ];

  private static async Task ApplyRetentionAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      DateTimeOffset receivedAt,
      HistoryRetentionPolicy retention,
      CancellationToken cancellationToken)
  {
    var node = nodeId.ToString("D");
    await SweepAsync(
        connection,
        transaction,
        node,
        receivedAt,
        retention,
        cancellationToken);
    if (await ShouldSweepGloballyAsync(
        connection,
        transaction,
        receivedAt,
        retention.GlobalSweepInterval,
        cancellationToken))
    {
      await SweepAsync(
          connection,
          transaction,
          null,
          receivedAt,
          retention,
          cancellationToken);
      await BoundHistoryNodesAsync(
          connection,
          transaction,
          receivedAt,
          retention.MaximumHistoryNodes,
          cancellationToken);
      await BoundProfileHistoriesAsync(
          connection,
          transaction,
          null,
          receivedAt,
          retention.MaximumProfileHistories,
          cancellationToken);
      await ExpireTombstonesAsync(
          connection,
          transaction,
          receivedAt,
          retention,
          cancellationToken);
    }
  }

  /// <summary>
  /// Ages and bounds retained rows for one node, or for the whole database when no node is scoped.
  /// </summary>
  private static async Task SweepAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string? nodeId,
      DateTimeOffset receivedAt,
      HistoryRetentionPolicy retention,
      CancellationToken cancellationToken)
  {
    var isGlobal = nodeId is null;
    var before = await CountRowsAsync(
        connection,
        transaction,
        nodeId,
        cancellationToken);

    await DeleteOlderThanAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[0],
        receivedAt - retention.SampleRetention,
        cancellationToken);
    await DeleteOlderThanAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[1],
        receivedAt - retention.RollupRetention,
        cancellationToken);
    await DeleteOlderThanAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[2],
        receivedAt - retention.EventRetention,
        cancellationToken);
    await DeleteOlderThanAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[3],
        receivedAt - retention.DiagnosticRetention,
        cancellationToken);
    await DeleteOlderThanAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[4],
        receivedAt - retention.DiagnosticRetention,
        cancellationToken);

    await BoundRowsAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[0],
        HistoryPartition.Profile,
        retention.MaximumSamplesPerProfile,
        cancellationToken);
    await BoundRowsAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[2],
        HistoryPartition.Profile,
        retention.MaximumEventsPerProfile,
        cancellationToken);
    await BoundRowsAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[3],
        HistoryPartition.Profile,
        retention.MaximumDiagnosticsPerProfile,
        cancellationToken);
    await BoundRowsAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[4],
        HistoryPartition.Profile,
        retention.MaximumDiagnosticsPerProfile,
        cancellationToken);

    var rowPartition = isGlobal
        ? HistoryPartition.Database
        : HistoryPartition.Node;
    await BoundRowsAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[0],
        rowPartition,
        isGlobal
            ? retention.MaximumSamplesPerDatabase
            : retention.MaximumSamplesPerNode,
        cancellationToken);
    await BoundRowsAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[1],
        rowPartition,
        isGlobal
            ? retention.MaximumRollupsPerDatabase
            : retention.MaximumRollupsPerNode,
        cancellationToken);
    await BoundRowsAsync(
        connection,
        transaction,
        nodeId,
        CountedTables[2],
        rowPartition,
        isGlobal
            ? retention.MaximumEventsPerDatabase
            : retention.MaximumEventsPerNode,
        cancellationToken);
    await BoundDiagnosticsAsync(
        connection,
        transaction,
        nodeId,
        rowPartition,
        isGlobal
            ? retention.MaximumDiagnosticsPerDatabase
            : retention.MaximumDiagnosticsPerNode,
        cancellationToken);

    var after = await CountRowsAsync(
        connection,
        transaction,
        nodeId,
        cancellationToken);
    await RecordDroppedAsync(
        connection,
        transaction,
        before,
        after,
        cancellationToken);

    if (!isGlobal)
    {
      await BoundProfileHistoriesAsync(
          connection,
          transaction,
          nodeId,
          receivedAt,
          retention.MaximumProfilesPerNode,
          cancellationToken);
    }

    await ExpireCursorsAsync(
        connection,
        transaction,
        nodeId,
        receivedAt,
        retention,
        cancellationToken);
  }

  /// <summary>
  /// Claims one bounded global sweep at most once per configured interval.
  /// </summary>
  private static async Task<bool> ShouldSweepGloballyAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      DateTimeOffset receivedAt,
      TimeSpan interval,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE history_maintenance
        SET last_swept_at = $now
        WHERE singleton = 0
          AND (last_swept_at IS NULL OR last_swept_at <= $threshold);
        """;
    command.Parameters.AddWithValue("$now", Utc(receivedAt));
    command.Parameters.AddWithValue("$threshold", Utc(receivedAt - interval));
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
  }

  private static async Task DeleteOlderThanAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string? nodeId,
      HistoryTable table,
      DateTimeOffset cutoff,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        DELETE FROM {table.Name}
        WHERE {table.TimeColumn} < $cutoff
          AND ($nodeId IS NULL OR node_id = $nodeId);
        """;
    command.Parameters.AddWithValue("$cutoff", Utc(cutoff));
    AddNullable(command, "$nodeId", nodeId);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  /// <summary>
  /// Deletes every row beyond the newest configured count inside one deterministic ranking.
  /// </summary>
  /// <remarks>
  /// The ranking orders by the retained timestamp and then by the remaining primary-key columns, so
  /// rows that share a timestamp or an hourly bucket are ordered totally. Deletion then matches the
  /// full primary key, which keeps exactly the configured newest count instead of deleting every
  /// tied row or none of them.
  /// </remarks>
  private static async Task BoundRowsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string? nodeId,
      HistoryTable table,
      HistoryPartition partition,
      int maximum,
      CancellationToken cancellationToken)
  {
    var partitionClause = partition switch
    {
      HistoryPartition.Profile => "PARTITION BY node_id, profile_id ",
      HistoryPartition.Node => "PARTITION BY node_id ",
      _ => string.Empty,
    };
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        DELETE FROM {table.Name}
        WHERE (node_id, profile_id, {table.KeyColumns}) IN (
            SELECT node_id, profile_id, {table.KeyColumns}
            FROM (
                SELECT
                    node_id,
                    profile_id,
                    {table.KeyColumns},
                    ROW_NUMBER() OVER (
                        {partitionClause}ORDER BY {table.OrderBy}) AS rank_index
                FROM {table.Name}
                WHERE ($nodeId IS NULL OR node_id = $nodeId))
            WHERE rank_index > $maximum);
        """;
    AddNullable(command, "$nodeId", nodeId);
    command.Parameters.AddWithValue("$maximum", maximum);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  /// <summary>
  /// Applies one combined diagnostic budget shared by subsystem health and capacity deficits.
  /// </summary>
  /// <remarks>
  /// Ranking both collections together keeps the advertised node-wide and database-wide ceilings
  /// truthful: neither collection can consume the whole budget twice.
  /// </remarks>
  private static async Task BoundDiagnosticsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string? nodeId,
      HistoryPartition partition,
      int maximum,
      CancellationToken cancellationToken)
  {
    var partitionClause = partition == HistoryPartition.Node
        ? "PARTITION BY node_id "
        : string.Empty;
    var ranked =
        $"""
        WITH combined AS (
            SELECT
                'a' AS kind,
                node_id,
                profile_id,
                subsystem AS key_value,
                observed_at
            FROM {SubsystemHealthTable}
            WHERE ($nodeId IS NULL OR node_id = $nodeId)
            UNION ALL
            SELECT
                'b' AS kind,
                node_id,
                profile_id,
                target_key AS key_value,
                observed_at
            FROM {CapacityDeficitTable}
            WHERE ($nodeId IS NULL OR node_id = $nodeId)),
        ranked AS (
            SELECT
                kind,
                node_id,
                profile_id,
                key_value,
                observed_at,
                ROW_NUMBER() OVER (
                    {partitionClause}ORDER BY
                        observed_at DESC,
                        kind ASC,
                        profile_id ASC,
                        key_value ASC) AS rank_index
            FROM combined)
        """;
    await ExecuteDiagnosticEvictionAsync(
        connection,
        transaction,
        $"""
        {ranked}
        DELETE FROM {SubsystemHealthTable}
        WHERE (node_id, profile_id, subsystem, observed_at) IN (
            SELECT node_id, profile_id, key_value, observed_at
            FROM ranked
            WHERE kind = 'a' AND rank_index > $maximum);
        """,
        nodeId,
        maximum,
        cancellationToken);
    await ExecuteDiagnosticEvictionAsync(
        connection,
        transaction,
        $"""
        {ranked}
        DELETE FROM {CapacityDeficitTable}
        WHERE (node_id, profile_id, target_key, observed_at) IN (
            SELECT node_id, profile_id, key_value, observed_at
            FROM ranked
            WHERE kind = 'b' AND rank_index > $maximum);
        """,
        nodeId,
        maximum,
        cancellationToken);
  }

  private static async Task ExecuteDiagnosticEvictionAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string commandText,
      string? nodeId,
      int maximum,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = commandText;
    AddNullable(command, "$nodeId", nodeId);
    command.Parameters.AddWithValue("$maximum", maximum);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<Dictionary<HistoryProfileKey, long[]>>
      CountRowsAsync(
          SqliteConnection connection,
          SqliteTransaction transaction,
          string? nodeId,
          CancellationToken cancellationToken)
  {
    var counts = new Dictionary<HistoryProfileKey, long[]>();
    for (var index = 0; index < CountedTables.Length; index++)
    {
      await using var command = connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText =
          $"""
          SELECT node_id, profile_id, COUNT(*)
          FROM {CountedTables[index].Name}
          WHERE ($nodeId IS NULL OR node_id = $nodeId)
          GROUP BY node_id, profile_id;
          """;
      AddNullable(command, "$nodeId", nodeId);
      await using var reader = await command.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        var key = new HistoryProfileKey(
            reader.GetString(0),
            reader.GetString(1));
        if (!counts.TryGetValue(key, out var totals))
        {
          totals = new long[CountedTables.Length];
          counts[key] = totals;
        }

        totals[index] = reader.GetInt64(2);
      }
    }

    return counts;
  }

  private static async Task RecordDroppedAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Dictionary<HistoryProfileKey, long[]> before,
      Dictionary<HistoryProfileKey, long[]> after,
      CancellationToken cancellationToken)
  {
    foreach (var (key, previous) in before)
    {
      var remaining = after.GetValueOrDefault(key)
          ?? new long[CountedTables.Length];
      var samples = previous[0] - remaining[0];
      var rollups = previous[1] - remaining[1];
      var events = previous[2] - remaining[2];
      var health = previous[3] - remaining[3];
      var deficits = previous[4] - remaining[4];
      if (samples == 0 &&
          rollups == 0 &&
          events == 0 &&
          health == 0 &&
          deficits == 0)
      {
        continue;
      }

      await using var command = connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText =
          """
          UPDATE profile_history_cursors
          SET dropped_samples = dropped_samples + $droppedSamples,
              dropped_rollups = dropped_rollups + $droppedRollups,
              dropped_events = dropped_events + $droppedEvents,
              dropped_subsystem_health =
                  dropped_subsystem_health + $droppedHealth,
              dropped_capacity_deficits =
                  dropped_capacity_deficits + $droppedDeficits
          WHERE node_id = $nodeId
            AND profile_id = $profileId;
          """;
      command.Parameters.AddWithValue("$nodeId", key.NodeId);
      command.Parameters.AddWithValue("$profileId", key.ProfileId);
      command.Parameters.AddWithValue("$droppedSamples", samples);
      command.Parameters.AddWithValue("$droppedRollups", rollups);
      command.Parameters.AddWithValue("$droppedEvents", events);
      command.Parameters.AddWithValue("$droppedHealth", health);
      command.Parameters.AddWithValue("$droppedDeficits", deficits);
      await command.ExecuteNonQueryAsync(cancellationToken);
    }
  }

  /// <summary>
  /// Bounds how many profile histories are retained per node, or across the database.
  /// </summary>
  private static async Task BoundProfileHistoriesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string? nodeId,
      DateTimeOffset receivedAt,
      int maximum,
      CancellationToken cancellationToken)
  {
    var partitionClause = nodeId is null
        ? string.Empty
        : "PARTITION BY node_id ";
    var excess = new List<HistoryProfileKey>();
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          $"""
          SELECT node_id, profile_id
          FROM (
              SELECT
                  node_id,
                  profile_id,
                  ROW_NUMBER() OVER (
                      {partitionClause}ORDER BY
                          updated_at DESC,
                          node_id ASC,
                          profile_id ASC) AS rank_index
              FROM profile_history_cursors
              WHERE ($nodeId IS NULL OR node_id = $nodeId))
          WHERE rank_index > $maximum;
          """;
      AddNullable(command, "$nodeId", nodeId);
      command.Parameters.AddWithValue("$maximum", maximum);
      await using var reader = await command.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        excess.Add(new HistoryProfileKey(
            reader.GetString(0),
            reader.GetString(1)));
      }
    }

    foreach (var key in excess)
    {
      await EvictProfileHistoryAsync(
          connection,
          transaction,
          key,
          receivedAt,
          cancellationToken);
    }
  }

  /// <summary>
  /// Bounds how many nodes retain history at once so node churn cannot grow the database forever.
  /// </summary>
  private static async Task BoundHistoryNodesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      DateTimeOffset receivedAt,
      int maximum,
      CancellationToken cancellationToken)
  {
    var excess = new List<string>();
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          SELECT node_id
          FROM (
              SELECT
                  node_id,
                  ROW_NUMBER() OVER (
                      ORDER BY newest DESC, node_id ASC) AS rank_index
              FROM (
                  SELECT node_id, MAX(updated_at) AS newest
                  FROM profile_history_cursors
                  GROUP BY node_id))
          WHERE rank_index > $maximum;
          """;
      command.Parameters.AddWithValue("$maximum", maximum);
      await using var reader = await command.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        excess.Add(reader.GetString(0));
      }
    }

    foreach (var node in excess)
    {
      var profiles = new List<HistoryProfileKey>();
      await using (var command = connection.CreateCommand())
      {
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT profile_id
            FROM profile_history_cursors
            WHERE node_id = $nodeId;
            """;
        command.Parameters.AddWithValue("$nodeId", node);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
          profiles.Add(new HistoryProfileKey(node, reader.GetString(0)));
        }
      }

      foreach (var key in profiles)
      {
        await EvictProfileHistoryAsync(
            connection,
            transaction,
            key,
            receivedAt,
            cancellationToken);
      }
    }
  }

  /// <summary>
  /// Deletes every retained row of one profile and preserves its provenance in a tombstone.
  /// </summary>
  private static async Task EvictProfileHistoryAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      HistoryProfileKey key,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken)
  {
    await CountEvictedRowsAsync(
        connection,
        transaction,
        key,
        cancellationToken);
    await WriteTombstoneAsync(
        connection,
        transaction,
        key,
        receivedAt,
        cancellationToken);
    foreach (var table in ProfileScopedTables)
    {
      await using var command = connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText =
          $"""
          DELETE FROM {table}
          WHERE node_id = $nodeId
            AND profile_id = $profileId;
          """;
      command.Parameters.AddWithValue("$nodeId", key.NodeId);
      command.Parameters.AddWithValue("$profileId", key.ProfileId);
      await command.ExecuteNonQueryAsync(cancellationToken);
    }

    await using var cursorCommand = connection.CreateCommand();
    cursorCommand.Transaction = transaction;
    cursorCommand.CommandText =
        """
        DELETE FROM profile_history_cursors
        WHERE node_id = $nodeId
          AND profile_id = $profileId;
        """;
    cursorCommand.Parameters.AddWithValue("$nodeId", key.NodeId);
    cursorCommand.Parameters.AddWithValue("$profileId", key.ProfileId);
    await cursorCommand.ExecuteNonQueryAsync(cancellationToken);
  }

  /// <summary>
  /// Accounts the rows an eviction is about to delete before the cursor becomes a tombstone.
  /// </summary>
  private static async Task CountEvictedRowsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      HistoryProfileKey key,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        UPDATE profile_history_cursors
        SET dropped_samples = dropped_samples + (
                SELECT COUNT(*) FROM {CountedTables[0].Name}
                WHERE node_id = $nodeId AND profile_id = $profileId),
            dropped_rollups = dropped_rollups + (
                SELECT COUNT(*) FROM {CountedTables[1].Name}
                WHERE node_id = $nodeId AND profile_id = $profileId),
            dropped_events = dropped_events + (
                SELECT COUNT(*) FROM {CountedTables[2].Name}
                WHERE node_id = $nodeId AND profile_id = $profileId),
            dropped_subsystem_health = dropped_subsystem_health + (
                SELECT COUNT(*) FROM {CountedTables[3].Name}
                WHERE node_id = $nodeId AND profile_id = $profileId),
            dropped_capacity_deficits = dropped_capacity_deficits + (
                SELECT COUNT(*) FROM {CountedTables[4].Name}
                WHERE node_id = $nodeId AND profile_id = $profileId)
        WHERE node_id = $nodeId
          AND profile_id = $profileId;
        """;
    command.Parameters.AddWithValue("$nodeId", key.NodeId);
    command.Parameters.AddWithValue("$profileId", key.ProfileId);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task WriteTombstoneAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      HistoryProfileKey key,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO profile_history_tombstones (
            node_id,
            profile_id,
            expired_at,
            epoch,
            epoch_resets,
            sample_high_water,
            stored_highest_sequence,
            manager_dropped_events,
            missed_events,
            dropped_samples,
            dropped_rollups,
            dropped_events,
            dropped_subsystem_health,
            dropped_capacity_deficits,
            rejected_future_samples,
            rejected_future_events)
        SELECT
            node_id,
            profile_id,
            $expiredAt,
            epoch,
            epoch_resets,
            sample_high_water,
            stored_highest_sequence,
            manager_dropped_events,
            missed_events,
            dropped_samples,
            dropped_rollups,
            dropped_events,
            dropped_subsystem_health,
            dropped_capacity_deficits,
            rejected_future_samples,
            rejected_future_events
        FROM profile_history_cursors
        WHERE node_id = $nodeId
          AND profile_id = $profileId
        ON CONFLICT (node_id, profile_id) DO UPDATE SET
            expired_at = excluded.expired_at,
            epoch = MAX(profile_history_tombstones.epoch, excluded.epoch),
            epoch_resets = excluded.epoch_resets,
            -- SQLite scalar MAX yields NULL when any argument is NULL, so each side falls back to
            -- the other before the comparison. A tombstone therefore never loses a known high-water
            -- or a known highest sequence to an unknown one.
            sample_high_water = MAX(
                COALESCE(
                    profile_history_tombstones.sample_high_water,
                    excluded.sample_high_water),
                COALESCE(
                    excluded.sample_high_water,
                    profile_history_tombstones.sample_high_water)),
            stored_highest_sequence = MAX(
                COALESCE(
                    profile_history_tombstones.stored_highest_sequence,
                    excluded.stored_highest_sequence),
                COALESCE(
                    excluded.stored_highest_sequence,
                    profile_history_tombstones.stored_highest_sequence)),
            manager_dropped_events = excluded.manager_dropped_events,
            missed_events = excluded.missed_events,
            dropped_samples = excluded.dropped_samples,
            dropped_rollups = excluded.dropped_rollups,
            dropped_events = excluded.dropped_events,
            dropped_subsystem_health = excluded.dropped_subsystem_health,
            dropped_capacity_deficits = excluded.dropped_capacity_deficits,
            rejected_future_samples = excluded.rejected_future_samples,
            rejected_future_events = excluded.rejected_future_events;
        """;
    command.Parameters.AddWithValue("$nodeId", key.NodeId);
    command.Parameters.AddWithValue("$profileId", key.ProfileId);
    command.Parameters.AddWithValue("$expiredAt", Utc(receivedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  /// <summary>
  /// Expires a cursor only once its latest projection and every derived row are gone.
  /// </summary>
  /// <remarks>
  /// The durable sample high-water and durable epoch survive the expiry inside a tombstone, so a
  /// stale heartbeat arriving after retention deleted the raw rows cannot reinsert an old sample,
  /// inflate an hourly rollup, or make a returning profile look pristine.
  /// </remarks>
  private static async Task ExpireCursorsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string? nodeId,
      DateTimeOffset receivedAt,
      HistoryRetentionPolicy retention,
      CancellationToken cancellationToken)
  {
    var cutoff = Utc(receivedAt - LongestRetention(retention));
    var expired = new List<HistoryProfileKey>();
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          $"""
          SELECT c.node_id, c.profile_id
          FROM profile_history_cursors AS c
          WHERE ($nodeId IS NULL OR c.node_id = $nodeId)
            AND c.updated_at < $cutoff
            AND NOT EXISTS (
                SELECT 1
                FROM profiles AS p
                WHERE p.node_id = c.node_id
                  AND p.profile_id = c.profile_id)
            AND NOT EXISTS (
                SELECT 1 FROM {CountedTables[0].Name} AS s
                WHERE s.node_id = c.node_id AND s.profile_id = c.profile_id)
            AND NOT EXISTS (
                SELECT 1 FROM {CountedTables[1].Name} AS r
                WHERE r.node_id = c.node_id AND r.profile_id = c.profile_id)
            AND NOT EXISTS (
                SELECT 1 FROM {CountedTables[2].Name} AS e
                WHERE e.node_id = c.node_id AND e.profile_id = c.profile_id)
            AND NOT EXISTS (
                SELECT 1 FROM {CountedTables[3].Name} AS h
                WHERE h.node_id = c.node_id AND h.profile_id = c.profile_id)
            AND NOT EXISTS (
                SELECT 1 FROM {CountedTables[4].Name} AS d
                WHERE d.node_id = c.node_id AND d.profile_id = c.profile_id);
          """;
      AddNullable(command, "$nodeId", nodeId);
      command.Parameters.AddWithValue("$cutoff", cutoff);
      await using var reader = await command.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        expired.Add(new HistoryProfileKey(
            reader.GetString(0),
            reader.GetString(1)));
      }
    }

    foreach (var key in expired)
    {
      await EvictProfileHistoryAsync(
          connection,
          transaction,
          key,
          receivedAt,
          cancellationToken);
    }
  }

  /// <summary>
  /// Deletes tombstones only once no query window can still reach the data they describe.
  /// </summary>
  private static async Task ExpireTombstonesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      DateTimeOffset receivedAt,
      HistoryRetentionPolicy retention,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        DELETE FROM profile_history_tombstones
        WHERE expired_at < $cutoff;
        """;
    command.Parameters.AddWithValue(
        "$cutoff",
        Utc(receivedAt - LongestRetention(retention)));
    await command.ExecuteNonQueryAsync(cancellationToken);

    await BoundTombstonesAsync(
        connection,
        transaction,
        "PARTITION BY node_id ",
        retention.MaximumProfilesPerNode,
        cancellationToken);
    await BoundTombstonesAsync(
        connection,
        transaction,
        string.Empty,
        retention.MaximumProfileHistories,
        cancellationToken);
  }

  /// <summary>
  /// Bounds how many retention-loss tombstones are kept so profile churn cannot grow them forever.
  /// </summary>
  /// <remarks>
  /// A tombstone reports honest retention loss, but it is still stored data. Ranking by expiry time
  /// and then by the full key keeps exactly the configured newest count even when many profiles were
  /// expired by the same sweep and therefore share one expiry timestamp.
  /// </remarks>
  private static async Task BoundTombstonesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string partitionClause,
      int maximum,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        DELETE FROM profile_history_tombstones
        WHERE (node_id, profile_id) IN (
            SELECT node_id, profile_id
            FROM (
                SELECT
                    node_id,
                    profile_id,
                    ROW_NUMBER() OVER (
                        {partitionClause}ORDER BY
                            expired_at DESC,
                            node_id ASC,
                            profile_id ASC) AS rank_index
                FROM profile_history_tombstones)
            WHERE rank_index > $maximum);
        """;
    command.Parameters.AddWithValue("$maximum", maximum);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static TimeSpan LongestRetention(HistoryRetentionPolicy retention)
  {
    var longest = retention.SampleRetention;
    if (retention.RollupRetention > longest)
    {
      longest = retention.RollupRetention;
    }
    if (retention.EventRetention > longest)
    {
      longest = retention.EventRetention;
    }
    if (retention.DiagnosticRetention > longest)
    {
      longest = retention.DiagnosticRetention;
    }

    return longest;
  }

  private enum HistoryPartition
  {
    Profile,
    Node,
    Database,
  }

  private sealed record HistoryProfileKey(string NodeId, string ProfileId);

  private sealed record HistoryTable(
      string Name,
      string TimeColumn,
      string KeyColumns)
  {
    public string OrderBy { get; } = string.Join(
        ", ",
        new[] { TimeColumn }
            .Concat(KeyColumns
                .Split(',')
                .Select(column => column.Trim())
                .Where(column => !string.Equals(
                    column,
                    TimeColumn,
                    StringComparison.Ordinal)))
            .Select(column => $"{column} DESC"));
  }
}
