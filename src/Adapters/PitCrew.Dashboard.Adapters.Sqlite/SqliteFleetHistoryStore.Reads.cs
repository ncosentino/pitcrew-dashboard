using System.Globalization;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed partial class SqliteFleetHistoryStore
{
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
    var pointTotals = await LoadTotalsAsync(
        connection,
        sqliteTransaction,
        window.Resolution == HistoryResolution.Hourly
            ? "profile_telemetry_rollups"
            : "profile_telemetry_samples",
        window.Resolution == HistoryResolution.Hourly
            ? "bucket_start"
            : "observed_at",
        nodeId,
        profileId,
        window,
        cancellationToken);
    var eventTotals = await LoadTotalsAsync(
        connection,
        sqliteTransaction,
        "profile_manager_events",
        "observed_at",
        nodeId,
        profileId,
        window,
        cancellationToken);
    var healthTotals = await LoadTotalsAsync(
        connection,
        sqliteTransaction,
        SubsystemHealthTable,
        "observed_at",
        nodeId,
        profileId,
        window,
        cancellationToken);
    var deficitTotals = await LoadTotalsAsync(
        connection,
        sqliteTransaction,
        CapacityDeficitTable,
        "observed_at",
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
    profileIds.UnionWith(pointTotals.Keys);
    profileIds.UnionWith(eventTotals.Keys);
    profileIds.UnionWith(healthTotals.Keys);
    profileIds.UnionWith(deficitTotals.Keys);

    var histories = new List<ProfileHistory>(profileIds.Count);
    var pointsTruncated = false;
    var eventsTruncated = false;
    var diagnosticsTruncated = false;
    foreach (var id in profileIds)
    {
      var profileSamples = samples.GetValueOrDefault(id) ?? [];
      var profileRollups = rollups.GetValueOrDefault(id) ?? [];
      var profileEvents = events.GetValueOrDefault(id) ?? [];
      var profileHealth = health.GetValueOrDefault(id) ?? [];
      var profileDeficits = deficits.GetValueOrDefault(id) ?? [];
      var returnedPoints = window.Resolution == HistoryResolution.Hourly
          ? profileRollups.Count
          : profileSamples.Count;
      var profilePointsTruncated =
          returnedPoints < pointTotals.GetValueOrDefault(id);
      var profileEventsTruncated =
          profileEvents.Count < eventTotals.GetValueOrDefault(id);
      var profileHealthTruncated =
          profileHealth.Count < healthTotals.GetValueOrDefault(id);
      var profileDeficitsTruncated =
          profileDeficits.Count < deficitTotals.GetValueOrDefault(id);
      pointsTruncated |= profilePointsTruncated;
      eventsTruncated |= profileEventsTruncated;
      diagnosticsTruncated |=
          profileHealthTruncated || profileDeficitsTruncated;
      var journal = journals.GetValueOrDefault(id);
      histories.Add(new ProfileHistory(
          id,
          profileSamples,
          profileRollups,
          profileEvents,
          profileHealth,
          profileDeficits,
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
        window.DiagnosticLimit,
        window.NodePointLimit,
        window.NodeEventLimit,
        window.NodeDiagnosticLimit);
  }

  /// <summary>
  /// Counts every retained row of one collection inside the requested range, per profile.
  /// </summary>
  /// <remarks>
  /// Per-profile truncation is decided by comparing this total with what the response actually
  /// returned after the per-profile and node-wide ceilings, so a fully returned profile is never
  /// marked truncated because another profile was capped, and a profile whose rows were entirely
  /// displaced by the node-wide ceiling is still reported as truncated instead of defaulting to
  /// complete.
  /// </remarks>
  private static async Task<Dictionary<string, long>> LoadTotalsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string table,
      string timeColumn,
      Guid nodeId,
      string? profileId,
      HistoryWindow window,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        SELECT profile_id, COUNT(*)
        FROM {table}
        WHERE node_id = $nodeId
          AND {timeColumn} >= $from
          AND {timeColumn} < $to
          AND ($profileId IS NULL OR profile_id = $profileId)
        GROUP BY profile_id;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    AddNullable(command, "$profileId", profileId);
    command.Parameters.AddWithValue("$from", Utc(window.From));
    command.Parameters.AddWithValue("$to", Utc(window.To));
    var totals = new Dictionary<string, long>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      totals[reader.GetString(0)] = reader.GetInt64(1);
    }

    return totals;
  }

  private static async Task<Dictionary<string, List<ProfileTelemetrySample>>>
      LoadSamplesAsync(
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
            {SampleColumns}
        FROM (
            SELECT
                profile_id,
                {SampleColumns},
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY observed_at DESC) AS row_index,
                ROW_NUMBER() OVER (
                    ORDER BY observed_at DESC, profile_id ASC) AS node_index
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
    var pages =
        new Dictionary<string, List<ProfileTelemetrySample>>(
            StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (!pages.TryGetValue(id, out var page))
      {
        page = [];
        pages[id] = page;
      }

      page.Add(new ProfileTelemetrySample(
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

  private static async Task<Dictionary<string, List<ProfileTelemetryRollup>>>
      LoadRollupsAsync(
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
            max_busy_runners
        FROM (
            SELECT
                *,
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY bucket_start DESC) AS row_index,
                ROW_NUMBER() OVER (
                    ORDER BY bucket_start DESC, profile_id ASC) AS node_index
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
    var pages =
        new Dictionary<string, List<ProfileTelemetryRollup>>(
            StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (!pages.TryGetValue(id, out var page))
      {
        page = [];
        pages[id] = page;
      }

      page.Add(new ProfileTelemetryRollup(
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

  private static async Task<Dictionary<string, List<ManagerEvent>>>
      LoadEventsAsync(
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
            evidence
        FROM (
            SELECT
                *,
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY observed_at DESC, epoch DESC, sequence DESC)
                    AS row_index,
                ROW_NUMBER() OVER (
                    ORDER BY
                        observed_at DESC,
                        epoch DESC,
                        sequence DESC,
                        profile_id ASC) AS node_index
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
    var pages =
        new Dictionary<string, List<ManagerEvent>>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (!pages.TryGetValue(id, out var page))
      {
        page = [];
        pages[id] = page;
      }

      page.Add(new ManagerEvent(
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

  /// <summary>
  /// Ranks both diagnostic collections inside one shared node-wide budget.
  /// </summary>
  /// <remarks>
  /// Subsystem-health changes and capacity-deficit observations compete for the same node-wide
  /// budget, so the advertised node-wide diagnostic cap is truthful instead of being applied twice.
  /// The per-profile ceiling stays separate for each collection so neither hides the other, and both
  /// per-profile ceilings are reported explicitly.
  /// </remarks>
  private const string RankedDiagnosticsSql =
      """
      WITH combined AS (
          SELECT
              'a' AS kind,
              profile_id,
              subsystem AS key_value,
              observed_at
          FROM profile_subsystem_health
          WHERE node_id = $nodeId
            AND observed_at >= $from
            AND observed_at < $to
            AND ($profileId IS NULL OR profile_id = $profileId)
          UNION ALL
          SELECT
              'b' AS kind,
              profile_id,
              target_key AS key_value,
              observed_at
          FROM profile_capacity_deficits
          WHERE node_id = $nodeId
            AND observed_at >= $from
            AND observed_at < $to
            AND ($profileId IS NULL OR profile_id = $profileId)),
      ranked AS (
          SELECT
              kind,
              profile_id,
              key_value,
              observed_at,
              ROW_NUMBER() OVER (
                  ORDER BY
                      observed_at DESC,
                      kind ASC,
                      profile_id ASC,
                      key_value ASC) AS node_index,
              ROW_NUMBER() OVER (
                  PARTITION BY kind, profile_id
                  ORDER BY observed_at DESC, key_value ASC) AS row_index
          FROM combined)
      """;

  private static async Task<Dictionary<string, List<ProfileSubsystemHealthChange>>>
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
        $"""
        {RankedDiagnosticsSql}
        SELECT
            h.profile_id AS profile_id,
            h.subsystem AS subsystem,
            h.observed_at AS observed_at,
            h.state AS state,
            h.consecutive_failures AS consecutive_failures,
            h.retry_at AS retry_at,
            h.last_success_operation AS last_success_operation,
            h.last_success_observed_at AS last_success_observed_at,
            h.last_success_reason AS last_success_reason,
            h.last_failure_operation AS last_failure_operation,
            h.last_failure_observed_at AS last_failure_observed_at,
            h.last_failure_reason AS last_failure_reason,
            h.last_failure_evidence AS last_failure_evidence
        FROM profile_subsystem_health AS h
        JOIN ranked AS r
          ON r.kind = 'a'
         AND r.profile_id = h.profile_id
         AND r.key_value = h.subsystem
         AND r.observed_at = h.observed_at
        WHERE h.node_id = $nodeId
          AND r.row_index <= $diagnosticLimit
          AND r.node_index <= $nodeDiagnosticLimit
        ORDER BY h.profile_id, h.observed_at;
        """;
    AddDiagnosticParameters(command, nodeId, profileId, window);
    var changes =
        new Dictionary<string, List<ProfileSubsystemHealthChange>>(
            StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (!changes.TryGetValue(id, out var page))
      {
        page = [];
        changes[id] = page;
      }

      page.Add(new ProfileSubsystemHealthChange(
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

  private static async Task<Dictionary<string, List<ProfileCapacityDeficitObservation>>>
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
        $"""
        {RankedDiagnosticsSql}
        SELECT
            d.profile_id AS profile_id,
            d.target_key AS target_key,
            d.observed_at AS observed_at,
            d.repository AS repository,
            d.freshness AS freshness,
            d.target_slots AS target_slots,
            d.active_workers AS active_workers,
            d.starting_workers AS starting_workers,
            d.draining_workers AS draining_workers,
            d.cleanup_pending_workers AS cleanup_pending_workers,
            d.eligible_workers AS eligible_workers,
            d.local_deficit AS local_deficit,
            d.eligibility_deficit AS eligibility_deficit,
            d.reason AS reason,
            d.evidence AS evidence
        FROM profile_capacity_deficits AS d
        JOIN ranked AS r
          ON r.kind = 'b'
         AND r.profile_id = d.profile_id
         AND r.key_value = d.target_key
         AND r.observed_at = d.observed_at
        WHERE d.node_id = $nodeId
          AND r.row_index <= $diagnosticLimit
          AND r.node_index <= $nodeDiagnosticLimit
        ORDER BY d.profile_id, d.observed_at, d.target_key;
        """;
    AddDiagnosticParameters(command, nodeId, profileId, window);
    var deficits =
        new Dictionary<string, List<ProfileCapacityDeficitObservation>>(
            StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (!deficits.TryGetValue(id, out var page))
      {
        page = [];
        deficits[id] = page;
      }

      page.Add(new ProfileCapacityDeficitObservation(
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
              row.Int64("rejected_future_samples"),
              null));
    }

    await LoadTombstonesAsync(
        connection,
        transaction,
        nodeId,
        profileId,
        journals,
        cancellationToken);
    return journals;
  }

  /// <summary>
  /// Reports profiles whose history was deliberately expired instead of hiding the loss.
  /// </summary>
  /// <remarks>
  /// Deleting a cursor would otherwise make a returning profile look pristine, so a tombstone keeps
  /// the completeness provenance — the dropped counters, the rejected counters, the durable epoch,
  /// and the durable sample high-water — until no query window can still reach the deleted data.
  /// </remarks>
  private static async Task LoadTombstonesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string? profileId,
      Dictionary<string, JournalPage> journals,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            profile_id,
            expired_at,
            epoch,
            epoch_resets,
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
        FROM profile_history_tombstones
        WHERE node_id = $nodeId
          AND ($profileId IS NULL OR profile_id = $profileId);
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    AddNullable(command, "$profileId", profileId);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var id = row.String("profile_id");
      if (journals.ContainsKey(id))
      {
        continue;
      }

      journals[id] = new JournalPage(
          new ProfileEventJournalState(
              "expired",
              0,
              null,
              null,
              row.OptionalInt64("stored_highest_sequence"),
              row.Int32("manager_dropped_events"),
              row.Int64("missed_events"),
              0,
              row.Int64("epoch"),
              row.Int64("epoch_resets"),
              row.Int64("rejected_future_events"),
              row.Time("expired_at")),
          new ProfileRetentionFloor(
              null,
              row.Int64("dropped_samples"),
              null,
              row.Int64("dropped_rollups"),
              null,
              row.Int64("dropped_events"),
              null,
              row.Int64("dropped_subsystem_health"),
              null,
              row.Int64("dropped_capacity_deficits"),
              row.Int64("rejected_future_samples"),
              row.Time("expired_at")));
    }
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
      new(null, 0, null, 0, null, 0, null, 0, null, 0, 0, null);

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

  private sealed record JournalPage(
      ProfileEventJournalState Journal,
      ProfileRetentionFloor Retention);
}
