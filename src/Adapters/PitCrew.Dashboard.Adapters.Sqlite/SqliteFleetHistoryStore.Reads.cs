using System.Globalization;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed partial class SqliteFleetHistoryStore
{
  private static readonly string[] ProfileScopedTables =
  [
      "profile_telemetry_samples",
      "profile_telemetry_rollups",
      "profile_manager_events",
      "profile_subsystem_health",
      "profile_capacity_deficits",
      "profile_history_cursors",
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
    var sampleNodeCutoff = await ReadNodeCutoffAsync(
        connection,
        transaction,
        node,
        "profile_telemetry_samples",
        "observed_at",
        retention.MaximumSamplesPerNode,
        cancellationToken);
    var rollupNodeCutoff = await ReadNodeCutoffAsync(
        connection,
        transaction,
        node,
        "profile_telemetry_rollups",
        "bucket_start",
        retention.MaximumRollupsPerNode,
        cancellationToken);
    var eventNodeCutoff = await ReadNodeCutoffAsync(
        connection,
        transaction,
        node,
        "profile_manager_events",
        "observed_at",
        retention.MaximumEventsPerNode,
        cancellationToken);

    var healthNodeCutoff = await ReadNodeCutoffAsync(
        connection,
        transaction,
        node,
        "profile_subsystem_health",
        "observed_at",
        retention.MaximumDiagnosticsPerNode,
        cancellationToken);
    var deficitNodeCutoff = await ReadNodeCutoffAsync(
        connection,
        transaction,
        node,
        "profile_capacity_deficits",
        "observed_at",
        retention.MaximumDiagnosticsPerNode,
        cancellationToken);

    var diagnosticCutoff = Utc(receivedAt - retention.DiagnosticRetention);
    var profiles = new List<string>();
    await using (var profileCommand = connection.CreateCommand())
    {
      profileCommand.Transaction = transaction;
      profileCommand.CommandText =
          """
          SELECT profile_id
          FROM profile_history_cursors
          WHERE node_id = $nodeId;
          """;
      profileCommand.Parameters.AddWithValue("$nodeId", node);
      await using var reader = await profileCommand.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        profiles.Add(reader.GetString(0));
      }
    }

    foreach (var profileId in profiles)
    {
      var droppedSamples = await DeleteAsync(
          connection,
          transaction,
          """
          DELETE FROM profile_telemetry_samples
          WHERE node_id = $nodeId
            AND profile_id = $profileId
            AND (observed_at < $cutoff
              OR ($nodeCutoff IS NOT NULL AND observed_at <= $nodeCutoff)
              OR observed_at NOT IN (
                  SELECT observed_at
                  FROM profile_telemetry_samples
                  WHERE node_id = $nodeId
                    AND profile_id = $profileId
                  ORDER BY observed_at DESC
                  LIMIT $maximum));
          """,
          node,
          profileId,
          Utc(receivedAt - retention.SampleRetention),
          sampleNodeCutoff,
          retention.MaximumSamplesPerProfile,
          cancellationToken);
      var droppedRollups = await DeleteAsync(
          connection,
          transaction,
          """
          DELETE FROM profile_telemetry_rollups
          WHERE node_id = $nodeId
            AND profile_id = $profileId
            AND (bucket_start < $cutoff
              OR ($nodeCutoff IS NOT NULL AND bucket_start <= $nodeCutoff)
              OR bucket_start NOT IN (
                  SELECT bucket_start
                  FROM profile_telemetry_rollups
                  WHERE node_id = $nodeId
                    AND profile_id = $profileId
                  ORDER BY bucket_start DESC
                  LIMIT $maximum));
          """,
          node,
          profileId,
          Utc(receivedAt - retention.RollupRetention),
          rollupNodeCutoff,
          retention.MaximumRollupsPerNode,
          cancellationToken);
      var droppedEvents = await DeleteAsync(
          connection,
          transaction,
          """
          DELETE FROM profile_manager_events
          WHERE node_id = $nodeId
            AND profile_id = $profileId
            AND (observed_at < $cutoff
              OR ($nodeCutoff IS NOT NULL AND observed_at <= $nodeCutoff)
              OR (epoch, sequence) NOT IN (
                  SELECT epoch, sequence
                  FROM profile_manager_events
                  WHERE node_id = $nodeId
                    AND profile_id = $profileId
                  ORDER BY observed_at DESC, epoch DESC, sequence DESC
                  LIMIT $maximum));
          """,
          node,
          profileId,
          Utc(receivedAt - retention.EventRetention),
          eventNodeCutoff,
          retention.MaximumEventsPerProfile,
          cancellationToken);
      var droppedHealth = await DeleteDiagnosticsAsync(
          connection,
          transaction,
          "profile_subsystem_health",
          "subsystem",
          node,
          profileId,
          diagnosticCutoff,
          healthNodeCutoff,
          retention.MaximumDiagnosticsPerProfile,
          cancellationToken);
      var droppedDeficits = await DeleteDiagnosticsAsync(
          connection,
          transaction,
          "profile_capacity_deficits",
          "target_key",
          node,
          profileId,
          diagnosticCutoff,
          deficitNodeCutoff,
          retention.MaximumDiagnosticsPerProfile,
          cancellationToken);

      if (droppedSamples == 0 &&
          droppedRollups == 0 &&
          droppedEvents == 0 &&
          droppedHealth == 0 &&
          droppedDeficits == 0)
      {
        continue;
      }

      await using var counters = connection.CreateCommand();
      counters.Transaction = transaction;
      counters.CommandText =
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
      counters.Parameters.AddWithValue("$nodeId", node);
      counters.Parameters.AddWithValue("$profileId", profileId);
      counters.Parameters.AddWithValue("$droppedSamples", droppedSamples);
      counters.Parameters.AddWithValue("$droppedRollups", droppedRollups);
      counters.Parameters.AddWithValue("$droppedEvents", droppedEvents);
      counters.Parameters.AddWithValue("$droppedHealth", droppedHealth);
      counters.Parameters.AddWithValue("$droppedDeficits", droppedDeficits);
      await counters.ExecuteNonQueryAsync(cancellationToken);
    }

    await BoundRetainedProfilesAsync(
        connection,
        transaction,
        node,
        retention.MaximumProfilesPerNode,
        cancellationToken);
    await ExpireCursorsAsync(
        connection,
        transaction,
        node,
        receivedAt,
        retention,
        cancellationToken);
  }

  /// <summary>
  /// Bounds retained diagnostic rows by age, by node-wide ceiling, and by per-profile ceiling.
  /// </summary>
  /// <remarks>
  /// Diagnostic rows are written on change, so no row is exempt from the age bound: a subsystem or
  /// autoscaling target key that stops being reported ages out like any other row instead of being
  /// preserved forever, which lets the profile cursor expire once every diagnostic row is gone. The
  /// per-profile and node-wide ceilings bound key churn while the profile keeps reporting.
  /// </remarks>
  private static async Task<int> DeleteDiagnosticsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string table,
      string keyColumn,
      string nodeId,
      string profileId,
      string cutoff,
      string? nodeCutoff,
      int maximumPerProfile,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        DELETE FROM {table}
        WHERE node_id = $nodeId
          AND profile_id = $profileId
          AND (observed_at < $cutoff
            OR ($nodeCutoff IS NOT NULL AND observed_at <= $nodeCutoff)
            OR (observed_at, {keyColumn}) NOT IN (
                SELECT observed_at, {keyColumn}
                FROM {table}
                WHERE node_id = $nodeId
                  AND profile_id = $profileId
                ORDER BY observed_at DESC
                LIMIT $maximum));
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId);
    command.Parameters.AddWithValue("$profileId", profileId);
    command.Parameters.AddWithValue("$cutoff", cutoff);
    AddNullable(command, "$nodeCutoff", nodeCutoff);
    command.Parameters.AddWithValue("$maximum", maximumPerProfile);
    return await command.ExecuteNonQueryAsync(cancellationToken);
  }

  /// <summary>
  /// Bounds how many profiles one node retains so profile identifier churn cannot grow forever.
  /// </summary>
  private static async Task BoundRetainedProfilesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string nodeId,
      int maximumProfiles,
      CancellationToken cancellationToken)
  {
    var excess = new List<string>();
    await using (var command = connection.CreateCommand())
    {
      command.Transaction = transaction;
      command.CommandText =
          """
          SELECT profile_id
          FROM profile_history_cursors
          WHERE node_id = $nodeId
          ORDER BY updated_at DESC, profile_id ASC
          LIMIT -1 OFFSET $maximum;
          """;
      command.Parameters.AddWithValue("$nodeId", nodeId);
      command.Parameters.AddWithValue("$maximum", maximumProfiles);
      await using var reader = await command.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        excess.Add(reader.GetString(0));
      }
    }

    foreach (var profileId in excess)
    {
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
        command.Parameters.AddWithValue("$nodeId", nodeId);
        command.Parameters.AddWithValue("$profileId", profileId);
        await command.ExecuteNonQueryAsync(cancellationToken);
      }
    }
  }

  private static async Task<string?> ReadNodeCutoffAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string nodeId,
      string table,
      string column,
      int maximumPerNode,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        SELECT {column}
        FROM {table}
        WHERE node_id = $nodeId
        ORDER BY {column} DESC
        LIMIT 1 OFFSET $maximum;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId);
    command.Parameters.AddWithValue("$maximum", maximumPerNode);
    return await command.ExecuteScalarAsync(cancellationToken) as string;
  }

  private static async Task<int> DeleteAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string commandText,
      string nodeId,
      string profileId,
      string cutoff,
      string? nodeCutoff,
      int maximum,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = commandText;
    command.Parameters.AddWithValue("$nodeId", nodeId);
    command.Parameters.AddWithValue("$profileId", profileId);
    command.Parameters.AddWithValue("$cutoff", cutoff);
    if (commandText.Contains("$nodeCutoff", StringComparison.Ordinal))
    {
      AddNullable(command, "$nodeCutoff", nodeCutoff);
      command.Parameters.AddWithValue("$maximum", maximum);
    }

    return await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task ExpireCursorsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string nodeId,
      DateTimeOffset receivedAt,
      HistoryRetentionPolicy retention,
      CancellationToken cancellationToken)
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

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        DELETE FROM profile_history_cursors
        WHERE node_id = $nodeId
          AND updated_at < $cutoff
          AND NOT EXISTS (
              SELECT 1
              FROM profile_telemetry_samples AS s
              WHERE s.node_id = profile_history_cursors.node_id
                AND s.profile_id = profile_history_cursors.profile_id)
          AND NOT EXISTS (
              SELECT 1
              FROM profile_telemetry_rollups AS r
              WHERE r.node_id = profile_history_cursors.node_id
                AND r.profile_id = profile_history_cursors.profile_id)
          AND NOT EXISTS (
              SELECT 1
              FROM profile_manager_events AS e
              WHERE e.node_id = profile_history_cursors.node_id
                AND e.profile_id = profile_history_cursors.profile_id)
          AND NOT EXISTS (
              SELECT 1
              FROM profile_subsystem_health AS h
              WHERE h.node_id = profile_history_cursors.node_id
                AND h.profile_id = profile_history_cursors.profile_id)
          AND NOT EXISTS (
              SELECT 1
              FROM profile_capacity_deficits AS d
              WHERE d.node_id = profile_history_cursors.node_id
                AND d.profile_id = profile_history_cursors.profile_id);
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId);
    command.Parameters.AddWithValue("$cutoff", Utc(receivedAt - longest));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private async Task<NodeHistoryResponse?> LoadHistoryAsync(
      string tenantId,
      Guid nodeId,
      string? profileId,
      HistoryWindow window,
      DateTimeOffset generatedAt,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(window);
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = await connection.BeginTransactionAsync(
        cancellationToken);
    var sqliteTransaction = (SqliteTransaction)transaction;
    await using (var ownershipCommand = connection.CreateCommand())
    {
      ownershipCommand.Transaction = sqliteTransaction;
      ownershipCommand.CommandText =
          """
          SELECT 1
          FROM nodes
          WHERE node_id = $nodeId
            AND tenant_id = $tenantId;
          """;
      ownershipCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      ownershipCommand.Parameters.AddWithValue("$tenantId", tenantId);
      if (await ownershipCommand.ExecuteScalarAsync(cancellationToken) is null)
      {
        return null;
      }
    }

    var samples = window.Resolution == HistoryResolution.Raw
        ? await LoadSamplesAsync(
            connection,
            sqliteTransaction,
            nodeId,
            profileId,
            window,
            cancellationToken)
        : [];
    var rollups = window.Resolution == HistoryResolution.Hourly
        ? await LoadRollupsAsync(
            connection,
            sqliteTransaction,
            nodeId,
            profileId,
            window,
            cancellationToken)
        : [];
    var events = await LoadEventsAsync(
        connection,
        sqliteTransaction,
        nodeId,
        profileId,
        window,
        cancellationToken);
    var health = await LoadSubsystemHealthAsync(
        connection,
        sqliteTransaction,
        nodeId,
        profileId,
        window,
        cancellationToken);
    var deficits = await LoadCapacityDeficitsAsync(
        connection,
        sqliteTransaction,
        nodeId,
        profileId,
        window,
        cancellationToken);
    var journals = await LoadJournalsAsync(
        connection,
        sqliteTransaction,
        nodeId,
        profileId,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    var profileIds = new SortedSet<string>(StringComparer.Ordinal);
    profileIds.UnionWith(journals.Keys);
    profileIds.UnionWith(samples.Keys);
    profileIds.UnionWith(rollups.Keys);
    profileIds.UnionWith(events.Keys);
    profileIds.UnionWith(health.Keys);
    profileIds.UnionWith(deficits.Keys);

    var histories = new List<ProfileHistory>(profileIds.Count);
    var pointsTruncated = false;
    var eventsTruncated = false;
    var diagnosticsTruncated = false;
    foreach (var id in profileIds)
    {
      var samplePage = samples.GetValueOrDefault(id);
      var rollupPage = rollups.GetValueOrDefault(id);
      var eventPage = events.GetValueOrDefault(id);
      var healthPage = health.GetValueOrDefault(id);
      var deficitPage = deficits.GetValueOrDefault(id);
      var profilePointsTruncated =
          (samplePage?.Truncated ?? false) || (rollupPage?.Truncated ?? false);
      var profileEventsTruncated = eventPage?.Truncated ?? false;
      var profileHealthTruncated = healthPage?.Truncated ?? false;
      var profileDeficitsTruncated = deficitPage?.Truncated ?? false;
      pointsTruncated |= profilePointsTruncated;
      eventsTruncated |= profileEventsTruncated;
      diagnosticsTruncated |=
          profileHealthTruncated || profileDeficitsTruncated;
      var journal = journals.GetValueOrDefault(id);
      histories.Add(new ProfileHistory(
          id,
          samplePage?.Samples ?? [],
          rollupPage?.Rollups ?? [],
          eventPage?.Events ?? [],
          healthPage?.Changes ?? [],
          deficitPage?.Observations ?? [],
          profilePointsTruncated,
          profileEventsTruncated,
          profileHealthTruncated,
          profileDeficitsTruncated,
          journal?.Journal ?? EmptyJournal(),
          journal?.Retention ?? EmptyRetention()));
    }

    return new NodeHistoryResponse(
        nodeId,
        generatedAt,
        window.From,
        window.To,
        window.Resolution == HistoryResolution.Hourly ? "hourly" : "raw",
        histories,
        pointsTruncated,
        eventsTruncated,
        diagnosticsTruncated,
        window.PointLimit,
        window.EventLimit,
        window.DiagnosticLimit,
        window.NodePointLimit,
        window.NodeEventLimit,
        window.NodeDiagnosticLimit);
  }

  private static async Task<Dictionary<string, SamplePage>> LoadSamplesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string? profileId,
      HistoryWindow window,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        SELECT
            profile_id,
            {SampleColumns},
            total_points,
            node_total
        FROM (
            SELECT
                profile_id,
                {SampleColumns},
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY observed_at DESC) AS row_index,
                COUNT(*) OVER (PARTITION BY profile_id) AS total_points,
                ROW_NUMBER() OVER (ORDER BY observed_at DESC) AS node_index,
                COUNT(*) OVER () AS node_total
            FROM profile_telemetry_samples
            WHERE node_id = $nodeId
              AND observed_at >= $from
              AND observed_at < $to
              AND ($profileId IS NULL OR profile_id = $profileId))
        WHERE row_index <= $pointLimit
          AND node_index <= $nodePointLimit
        ORDER BY profile_id, observed_at;
        """;
    AddWindowParameters(command, nodeId, profileId, window);
    var pages = new Dictionary<string, SamplePage>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (!pages.TryGetValue(id, out var page))
      {
        page = new SamplePage(
            [],
            row.Int64("total_points") > window.PointLimit ||
                row.Int64("node_total") > window.NodePointLimit);
        pages[id] = page;
      }

      page.Samples.Add(new ProfileTelemetrySample(
          row.Time("observed_at"),
          row.OptionalTime("sampled_at"),
          row.String("telemetry_status"),
          row.String("manager_instance_id"),
          row.String("manager_status"),
          row.Int32("generation"),
          row.Int32("desired_slots"),
          row.Int32("active_slots"),
          row.Int32("draining_slots"),
          row.OptionalInt32("configured_slots"),
          row.OptionalInt32("eligible_slots"),
          row.OptionalInt32("target_slots"),
          row.OptionalInt32("maximum_slots"),
          row.OptionalInt32("assigned_jobs"),
          row.OptionalInt32("running_jobs"),
          row.OptionalInt32("available_jobs"),
          row.OptionalInt32("idle_runners"),
          row.OptionalInt32("busy_runners"),
          row.Int32("local_running_workers"),
          row.OptionalDouble("manager_cpu_cores"),
          row.OptionalInt64("manager_memory_bytes"),
          row.OptionalInt32("manager_pids"),
          row.OptionalInt32("host_logical_processors"),
          row.OptionalInt64("host_memory_bytes"),
          row.OptionalDouble("worker_cpu_cores"),
          row.OptionalInt64("worker_memory_bytes"),
          row.OptionalInt32("worker_pids"),
          row.OptionalInt64("network_rx_bytes"),
          row.OptionalInt64("network_tx_bytes"),
          row.OptionalInt64("block_read_bytes"),
          row.OptionalInt64("block_write_bytes"),
          row.Int32("exit_reports"),
          row.Int32("adverse_exit_reports"),
          row.OptionalInt32("local_capacity_deficit"),
          row.OptionalInt32("eligibility_capacity_deficit"),
          row.OptionalString("capacity_deficit_reason"),
          row.OptionalString("capacity_deficit_freshness")));
    }

    return pages;
  }

  private static async Task<Dictionary<string, RollupPage>> LoadRollupsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string? profileId,
      HistoryWindow window,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            profile_id,
            bucket_start,
            sample_count,
            max_desired_slots,
            max_active_slots,
            max_draining_slots,
            max_eligible_slots,
            max_local_running_workers,
            max_manager_cpu_cores,
            max_manager_memory_bytes,
            max_manager_pids,
            max_worker_cpu_cores,
            max_worker_memory_bytes,
            max_worker_pids,
            max_network_rx_bytes,
            max_network_tx_bytes,
            max_block_read_bytes,
            max_block_write_bytes,
            max_exit_reports,
            max_adverse_exit_reports,
            max_local_capacity_deficit,
            max_eligibility_capacity_deficit,
            max_target_slots,
            max_assigned_jobs,
            max_idle_runners,
            max_busy_runners,
            total_points,
            node_total
        FROM (
            SELECT
                *,
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY bucket_start DESC) AS row_index,
                COUNT(*) OVER (PARTITION BY profile_id) AS total_points,
                ROW_NUMBER() OVER (ORDER BY bucket_start DESC) AS node_index,
                COUNT(*) OVER () AS node_total
            FROM profile_telemetry_rollups
            WHERE node_id = $nodeId
              AND bucket_start >= $from
              AND bucket_start < $to
              AND ($profileId IS NULL OR profile_id = $profileId))
        WHERE row_index <= $pointLimit
          AND node_index <= $nodePointLimit
        ORDER BY profile_id, bucket_start;
        """;
    AddWindowParameters(command, nodeId, profileId, window);
    var pages = new Dictionary<string, RollupPage>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (!pages.TryGetValue(id, out var page))
      {
        page = new RollupPage(
            [],
            row.Int64("total_points") > window.PointLimit ||
                row.Int64("node_total") > window.NodePointLimit);
        pages[id] = page;
      }

      page.Rollups.Add(new ProfileTelemetryRollup(
          row.Time("bucket_start"),
          row.Int32("sample_count"),
          row.Int32("max_desired_slots"),
          row.Int32("max_active_slots"),
          row.Int32("max_draining_slots"),
          row.OptionalInt32("max_eligible_slots"),
          row.Int32("max_local_running_workers"),
          row.OptionalDouble("max_manager_cpu_cores"),
          row.OptionalInt64("max_manager_memory_bytes"),
          row.OptionalInt32("max_manager_pids"),
          row.OptionalDouble("max_worker_cpu_cores"),
          row.OptionalInt64("max_worker_memory_bytes"),
          row.OptionalInt32("max_worker_pids"),
          row.OptionalInt64("max_network_rx_bytes"),
          row.OptionalInt64("max_network_tx_bytes"),
          row.OptionalInt64("max_block_read_bytes"),
          row.OptionalInt64("max_block_write_bytes"),
          row.Int32("max_exit_reports"),
          row.Int32("max_adverse_exit_reports"),
          row.OptionalInt32("max_local_capacity_deficit"),
          row.OptionalInt32("max_eligibility_capacity_deficit"),
          row.OptionalInt32("max_target_slots"),
          row.OptionalInt32("max_assigned_jobs"),
          row.OptionalInt32("max_idle_runners"),
          row.OptionalInt32("max_busy_runners")));
    }

    return pages;
  }

  private static async Task<Dictionary<string, EventPage>> LoadEventsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string? profileId,
      HistoryWindow window,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            profile_id,
            sequence,
            manager_instance_id,
            observed_at,
            subsystem,
            operation,
            target,
            outcome,
            duration_milliseconds,
            attempt,
            consecutive_failures,
            retry_at,
            reason,
            evidence,
            total_points,
            node_total
        FROM (
            SELECT
                *,
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY observed_at DESC, epoch DESC, sequence DESC)
                    AS row_index,
                COUNT(*) OVER (PARTITION BY profile_id) AS total_points,
                ROW_NUMBER() OVER (
                    ORDER BY observed_at DESC, epoch DESC, sequence DESC)
                    AS node_index,
                COUNT(*) OVER () AS node_total
            FROM profile_manager_events
            WHERE node_id = $nodeId
              AND observed_at >= $from
              AND observed_at < $to
              AND ($profileId IS NULL OR profile_id = $profileId))
        WHERE row_index <= $eventLimit
          AND node_index <= $nodeEventLimit
        ORDER BY profile_id, observed_at DESC, epoch DESC, sequence DESC;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$profileId",
        (object?)profileId ?? DBNull.Value);
    command.Parameters.AddWithValue("$from", Utc(window.From));
    command.Parameters.AddWithValue("$to", Utc(window.To));
    command.Parameters.AddWithValue("$eventLimit", window.EventLimit);
    command.Parameters.AddWithValue("$nodeEventLimit", window.NodeEventLimit);
    var pages = new Dictionary<string, EventPage>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (!pages.TryGetValue(id, out var page))
      {
        page = new EventPage(
            [],
            row.Int64("total_points") > window.EventLimit ||
                row.Int64("node_total") > window.NodeEventLimit);
        pages[id] = page;
      }

      page.Events.Add(new ManagerEvent(
          row.Int64("sequence"),
          row.String("manager_instance_id"),
          row.Time("observed_at"),
          row.String("subsystem"),
          row.String("operation"),
          row.OptionalString("target"),
          row.String("outcome"),
          row.OptionalInt32("duration_milliseconds"),
          row.OptionalInt32("attempt"),
          row.OptionalInt32("consecutive_failures"),
          row.OptionalTime("retry_at"),
          row.String("reason"),
          row.OptionalString("evidence")));
    }

    return pages;
  }

  private static async Task<Dictionary<string, SubsystemHealthPage>>
      LoadSubsystemHealthAsync(
          SqliteConnection connection,
          SqliteTransaction transaction,
          Guid nodeId,
          string? profileId,
          HistoryWindow window,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            profile_id,
            subsystem,
            observed_at,
            state,
            consecutive_failures,
            retry_at,
            last_success_operation,
            last_success_observed_at,
            last_success_reason,
            last_failure_operation,
            last_failure_observed_at,
            last_failure_reason,
            last_failure_evidence,
            total_points,
            node_total
        FROM (
            SELECT
                *,
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY observed_at DESC) AS row_index,
                COUNT(*) OVER (PARTITION BY profile_id) AS total_points,
                ROW_NUMBER() OVER (ORDER BY observed_at DESC) AS node_index,
                COUNT(*) OVER () AS node_total
            FROM profile_subsystem_health
            WHERE node_id = $nodeId
              AND observed_at >= $from
              AND observed_at < $to
              AND ($profileId IS NULL OR profile_id = $profileId))
        WHERE row_index <= $diagnosticLimit
          AND node_index <= $nodeDiagnosticLimit
        ORDER BY profile_id, observed_at;
        """;
    AddDiagnosticParameters(command, nodeId, profileId, window);
    var changes =
        new Dictionary<string, SubsystemHealthPage>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (!changes.TryGetValue(id, out var page))
      {
        page = new SubsystemHealthPage(
            [],
            row.Int64("total_points") > window.DiagnosticLimit ||
                row.Int64("node_total") > window.NodeDiagnosticLimit);
        changes[id] = page;
      }

      page.Changes.Add(new ProfileSubsystemHealthChange(
          row.String("subsystem"),
          row.Time("observed_at"),
          row.String("state"),
          row.Int32("consecutive_failures"),
          row.OptionalTime("retry_at"),
          row.OptionalString("last_success_operation"),
          row.OptionalTime("last_success_observed_at"),
          row.OptionalString("last_success_reason"),
          row.OptionalString("last_failure_operation"),
          row.OptionalTime("last_failure_observed_at"),
          row.OptionalString("last_failure_reason"),
          row.OptionalString("last_failure_evidence")));
    }

    return changes;
  }

  private static async Task<Dictionary<string, CapacityDeficitPage>>
      LoadCapacityDeficitsAsync(
          SqliteConnection connection,
          SqliteTransaction transaction,
          Guid nodeId,
          string? profileId,
          HistoryWindow window,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            profile_id,
            target_key,
            observed_at,
            repository,
            freshness,
            target_slots,
            active_workers,
            starting_workers,
            draining_workers,
            cleanup_pending_workers,
            eligible_workers,
            local_deficit,
            eligibility_deficit,
            reason,
            evidence,
            total_points,
            node_total
        FROM (
            SELECT
                *,
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY observed_at DESC) AS row_index,
                COUNT(*) OVER (PARTITION BY profile_id) AS total_points,
                ROW_NUMBER() OVER (ORDER BY observed_at DESC) AS node_index,
                COUNT(*) OVER () AS node_total
            FROM profile_capacity_deficits
            WHERE node_id = $nodeId
              AND observed_at >= $from
              AND observed_at < $to
              AND ($profileId IS NULL OR profile_id = $profileId))
        WHERE row_index <= $diagnosticLimit
          AND node_index <= $nodeDiagnosticLimit
        ORDER BY profile_id, observed_at, target_key;
        """;
    AddDiagnosticParameters(command, nodeId, profileId, window);
    var deficits =
        new Dictionary<string, CapacityDeficitPage>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (!deficits.TryGetValue(id, out var page))
      {
        page = new CapacityDeficitPage(
            [],
            row.Int64("total_points") > window.DiagnosticLimit ||
                row.Int64("node_total") > window.NodeDiagnosticLimit);
        deficits[id] = page;
      }

      page.Observations.Add(new ProfileCapacityDeficitObservation(
          row.String("target_key"),
          row.Time("observed_at"),
          row.OptionalString("repository"),
          row.String("freshness"),
          row.Int32("target_slots"),
          row.Int32("active_workers"),
          row.Int32("starting_workers"),
          row.Int32("draining_workers"),
          row.Int32("cleanup_pending_workers"),
          row.OptionalInt32("eligible_workers"),
          row.Int32("local_deficit"),
          row.OptionalInt32("eligibility_deficit"),
          row.String("reason"),
          row.OptionalString("evidence")));
    }

    return deficits;
  }

  private static async Task<Dictionary<string, JournalPage>> LoadJournalsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string? profileId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            c.profile_id AS profile_id,
            c.journal_status AS journal_status,
            c.journal_capacity AS journal_capacity,
            c.manager_highest_sequence AS manager_highest_sequence,
            c.manager_dropped_events AS manager_dropped_events,
            c.stored_highest_sequence AS stored_highest_sequence,
            c.missed_events AS missed_events,
            c.epoch AS epoch,
            c.epoch_resets AS epoch_resets,
            c.dropped_samples AS dropped_samples,
            c.dropped_rollups AS dropped_rollups,
            c.dropped_events AS dropped_events,
            c.dropped_subsystem_health AS dropped_subsystem_health,
            c.dropped_capacity_deficits AS dropped_capacity_deficits,
            c.rejected_future_samples AS rejected_future_samples,
            c.rejected_future_events AS rejected_future_events,
            c.updated_at AS updated_at,
            (SELECT MIN(e.sequence)
             FROM profile_manager_events AS e
             WHERE e.node_id = c.node_id
               AND e.profile_id = c.profile_id
               AND e.epoch = c.epoch) AS stored_lowest_sequence,
            (SELECT MIN(s.observed_at)
             FROM profile_telemetry_samples AS s
             WHERE s.node_id = c.node_id
               AND s.profile_id = c.profile_id) AS earliest_sample,
            (SELECT MIN(r.bucket_start)
             FROM profile_telemetry_rollups AS r
             WHERE r.node_id = c.node_id
               AND r.profile_id = c.profile_id) AS earliest_rollup,
            (SELECT MIN(v.observed_at)
             FROM profile_manager_events AS v
             WHERE v.node_id = c.node_id
               AND v.profile_id = c.profile_id) AS earliest_event,
            (SELECT MIN(h.observed_at)
             FROM profile_subsystem_health AS h
             WHERE h.node_id = c.node_id
               AND h.profile_id = c.profile_id) AS earliest_subsystem_health,
            (SELECT MIN(d.observed_at)
             FROM profile_capacity_deficits AS d
             WHERE d.node_id = c.node_id
               AND d.profile_id = c.profile_id) AS earliest_capacity_deficit
        FROM profile_history_cursors AS c
        WHERE c.node_id = $nodeId
          AND ($profileId IS NULL OR c.profile_id = $profileId);
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$profileId",
        (object?)profileId ?? DBNull.Value);
    var journals = new Dictionary<string, JournalPage>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var managerHighest = row.OptionalInt64("manager_highest_sequence");
      var storedHighest = row.OptionalInt64("stored_highest_sequence");
      var undelivered = managerHighest is null || storedHighest is null
          ? 0
          : Math.Max(0, managerHighest.Value - storedHighest.Value);
      journals[row.String("profile_id")] = new JournalPage(
          new ProfileEventJournalState(
              row.String("journal_status"),
              row.Int32("journal_capacity"),
              managerHighest,
              row.OptionalInt64("stored_lowest_sequence"),
              storedHighest,
              row.Int32("manager_dropped_events"),
              row.Int64("missed_events"),
              undelivered,
              row.Int64("epoch"),
              row.Int64("epoch_resets"),
              row.Int64("rejected_future_events"),
              row.Time("updated_at")),
          new ProfileRetentionFloor(
              row.OptionalTime("earliest_sample"),
              row.Int64("dropped_samples"),
              row.OptionalTime("earliest_rollup"),
              row.Int64("dropped_rollups"),
              row.OptionalTime("earliest_event"),
              row.Int64("dropped_events"),
              row.OptionalTime("earliest_subsystem_health"),
              row.Int64("dropped_subsystem_health"),
              row.OptionalTime("earliest_capacity_deficit"),
              row.Int64("dropped_capacity_deficits"),
              row.Int64("rejected_future_samples")));
    }

    return journals;
  }

  private static ProfileEventJournalState EmptyJournal() =>
      new(
          "unreported",
          0,
          null,
          null,
          null,
          0,
          0,
          0,
          0,
          0,
          0,
          null);

  private static ProfileRetentionFloor EmptyRetention() =>
      new(null, 0, null, 0, null, 0, null, 0, null, 0, 0);

  private static void AddWindowParameters(
      SqliteCommand command,
      Guid nodeId,
      string? profileId,
      HistoryWindow window)
  {
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$profileId",
        (object?)profileId ?? DBNull.Value);
    command.Parameters.AddWithValue("$from", Utc(window.From));
    command.Parameters.AddWithValue("$to", Utc(window.To));
    command.Parameters.AddWithValue("$pointLimit", window.PointLimit);
    command.Parameters.AddWithValue("$nodePointLimit", window.NodePointLimit);
  }

  private static void AddDiagnosticParameters(
      SqliteCommand command,
      Guid nodeId,
      string? profileId,
      HistoryWindow window)
  {
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$profileId",
        (object?)profileId ?? DBNull.Value);
    command.Parameters.AddWithValue("$from", Utc(window.From));
    command.Parameters.AddWithValue("$to", Utc(window.To));
    command.Parameters.AddWithValue(
        "$diagnosticLimit",
        window.DiagnosticLimit);
    command.Parameters.AddWithValue(
        "$nodeDiagnosticLimit",
        window.NodeDiagnosticLimit);
  }

  private static ProjectedSample ProjectSample(ManagerObservedState profile)
  {
    var localRunningWorkers = 0;
    double? workerCpuCores = null;
    long? workerMemoryBytes = null;
    int? workerPids = null;
    long? networkRxBytes = null;
    long? networkTxBytes = null;
    long? blockReadBytes = null;
    long? blockWriteBytes = null;
    var exitReports = 0;
    var adverseExitReports = 0;
    foreach (var slot in profile.Slots)
    {
      if (slot.ProcessRunning)
      {
        localRunningWorkers++;
      }
      if (slot.LastExit is not null)
      {
        exitReports++;
        if (!string.Equals(
            slot.LastExit.Classification,
            "clean",
            StringComparison.Ordinal))
        {
          adverseExitReports++;
        }
      }
      if (slot.Resources is null)
      {
        continue;
      }

      workerCpuCores = (workerCpuCores ?? 0) + slot.Resources.CpuCores;
      workerMemoryBytes =
          (workerMemoryBytes ?? 0) + slot.Resources.MemoryWorkingSetBytes;
      workerPids = (workerPids ?? 0) + slot.Resources.Pids;
      networkRxBytes = Accumulate(
          networkRxBytes,
          slot.Resources.NetworkRxBytes);
      networkTxBytes = Accumulate(
          networkTxBytes,
          slot.Resources.NetworkTxBytes);
      blockReadBytes = Accumulate(
          blockReadBytes,
          slot.Resources.BlockReadBytes);
      blockWriteBytes = Accumulate(
          blockWriteBytes,
          slot.Resources.BlockWriteBytes);
    }

    var deficit = SelectDeficit(profile.CapacityEvidence);
    return new ProjectedSample(
        Utc(profile.ObservedAt),
        profile.ResourceTelemetry is null
            ? null
            : Utc(profile.ResourceTelemetry.SampledAt),
        profile.ResourceTelemetry?.Status ?? "unreported",
        localRunningWorkers,
        workerCpuCores,
        workerMemoryBytes,
        workerPids,
        networkRxBytes,
        networkTxBytes,
        blockReadBytes,
        blockWriteBytes,
        exitReports,
        adverseExitReports,
        deficit is null || string.Equals(
            deficit.Freshness,
            "unavailable",
            StringComparison.Ordinal)
            ? null
            : deficit.LocalDeficit,
        deficit?.EligibilityDeficit,
        deficit?.Reason,
        deficit?.Freshness);
  }

  private static CapacityDeficitEvidence? SelectDeficit(
      ManagerCapacityEvidence? evidence)
  {
    if (evidence is null)
    {
      return null;
    }
    if (evidence.Fixed is not null)
    {
      return evidence.Fixed;
    }

    CapacityDeficitEvidence? selected = null;
    var selectedRank = int.MinValue;
    foreach (var target in evidence.Targets)
    {
      var rank = string.Equals(
          target.Freshness,
          "unavailable",
          StringComparison.Ordinal)
          ? int.MinValue + 1
          : target.LocalDeficit;
      if (selected is null || rank > selectedRank)
      {
        selected = target;
        selectedRank = rank;
      }
    }

    return selected;
  }

  private static long? Accumulate(long? total, long? measurement) =>
      measurement is null
          ? total
          : (total ?? 0) + measurement.Value;

  private sealed record ProjectedSample(
      string ObservedAt,
      string? SampledAt,
      string TelemetryStatus,
      int LocalRunningWorkers,
      double? WorkerCpuCores,
      long? WorkerMemoryBytes,
      int? WorkerPids,
      long? NetworkRxBytes,
      long? NetworkTxBytes,
      long? BlockReadBytes,
      long? BlockWriteBytes,
      int ExitReports,
      int AdverseExitReports,
      int? LocalCapacityDeficit,
      int? EligibilityCapacityDeficit,
      string? CapacityDeficitReason,
      string? CapacityDeficitFreshness);

  private sealed record SamplePage(
      List<ProfileTelemetrySample> Samples,
      bool Truncated);

  private sealed record RollupPage(
      List<ProfileTelemetryRollup> Rollups,
      bool Truncated);

  private sealed record EventPage(
      List<ManagerEvent> Events,
      bool Truncated);

  private sealed record SubsystemHealthPage(
      List<ProfileSubsystemHealthChange> Changes,
      bool Truncated);

  private sealed record CapacityDeficitPage(
      List<ProfileCapacityDeficitObservation> Observations,
      bool Truncated);

  private sealed record JournalPage(
      ProfileEventJournalState Journal,
      ProfileRetentionFloor Retention);
}
