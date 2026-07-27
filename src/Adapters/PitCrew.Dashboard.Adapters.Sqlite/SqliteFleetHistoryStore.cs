using System.Globalization;

using Microsoft.Data.Sqlite;
using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed class SqliteFleetHistoryStore(
    SqliteConnectionFactory _connectionFactory) : IFleetHistoryStore
{
  private const string SampleColumns =
      """
      observed_at,
      sampled_at,
      telemetry_status,
      manager_instance_id,
      manager_status,
      generation,
      desired_slots,
      active_slots,
      draining_slots,
      configured_slots,
      eligible_slots,
      target_slots,
      maximum_slots,
      assigned_jobs,
      running_jobs,
      available_jobs,
      idle_runners,
      busy_runners,
      local_running_workers,
      manager_cpu_cores,
      manager_memory_bytes,
      manager_pids,
      host_logical_processors,
      host_memory_bytes,
      worker_cpu_cores,
      worker_memory_bytes,
      worker_pids,
      network_rx_bytes,
      network_tx_bytes,
      block_read_bytes,
      block_write_bytes,
      exit_reports,
      adverse_exit_reports,
      local_capacity_deficit,
      eligibility_capacity_deficit,
      capacity_deficit_reason,
      capacity_deficit_freshness
      """;

  public async Task AppendAsync(
      Guid nodeId,
      IReadOnlyList<ManagerObservedState> profiles,
      DateTimeOffset receivedAt,
      HistoryRetentionPolicy retention,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(profiles);
    ArgumentNullException.ThrowIfNull(retention);
    if (profiles.Count == 0)
    {
      return;
    }

    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    foreach (var profile in profiles)
    {
      var appended = await AppendSampleAsync(
          connection,
          transaction,
          nodeId,
          profile,
          receivedAt,
          cancellationToken);
      if (appended)
      {
        await RecomputeRollupAsync(
            connection,
            transaction,
            nodeId,
            profile,
            cancellationToken);
      }

      await AppendEventsAsync(
          connection,
          transaction,
          nodeId,
          profile,
          receivedAt,
          cancellationToken);
      await ApplyRetentionAsync(
          connection,
          transaction,
          nodeId,
          profile.ProfileId,
          receivedAt,
          retention,
          cancellationToken);
    }

    await transaction.CommitAsync(cancellationToken);
  }

  public Task<NodeHistoryResponse?> GetNodeHistoryAsync(
      string tenantId,
      Guid nodeId,
      HistoryWindow window,
      DateTimeOffset generatedAt,
      CancellationToken cancellationToken) =>
      LoadHistoryAsync(
          tenantId,
          nodeId,
          null,
          window,
          generatedAt,
          cancellationToken);

  public Task<NodeHistoryResponse?> GetProfileHistoryAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      HistoryWindow window,
      DateTimeOffset generatedAt,
      CancellationToken cancellationToken) =>
      LoadHistoryAsync(
          tenantId,
          nodeId,
          profileId,
          window,
          generatedAt,
          cancellationToken);

  private static async Task<bool> AppendSampleAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      ManagerObservedState profile,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken)
  {
    var sample = ProjectSample(profile);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        INSERT INTO profile_telemetry_samples (
            node_id,
            profile_id,
            recorded_at,
            {SampleColumns})
        SELECT
            $nodeId,
            $profileId,
            $recordedAt,
            $observedAt,
            $sampledAt,
            $telemetryStatus,
            $managerInstanceId,
            $managerStatus,
            $generation,
            $desiredSlots,
            $activeSlots,
            $drainingSlots,
            $configuredSlots,
            $eligibleSlots,
            $targetSlots,
            $maximumSlots,
            $assignedJobs,
            $runningJobs,
            $availableJobs,
            $idleRunners,
            $busyRunners,
            $localRunningWorkers,
            $managerCpuCores,
            $managerMemoryBytes,
            $managerPids,
            $hostLogicalProcessors,
            $hostMemoryBytes,
            $workerCpuCores,
            $workerMemoryBytes,
            $workerPids,
            $networkRxBytes,
            $networkTxBytes,
            $blockReadBytes,
            $blockWriteBytes,
            $exitReports,
            $adverseExitReports,
            $localCapacityDeficit,
            $eligibilityCapacityDeficit,
            $capacityDeficitReason,
            $capacityDeficitFreshness
        WHERE NOT EXISTS (
            SELECT 1
            FROM profile_telemetry_samples
            WHERE node_id = $nodeId
              AND profile_id = $profileId
              AND observed_at >= $observedAt);
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profile.ProfileId);
    command.Parameters.AddWithValue("$recordedAt", Utc(receivedAt));
    command.Parameters.AddWithValue("$observedAt", sample.ObservedAt);
    AddNullable(command, "$sampledAt", sample.SampledAt);
    command.Parameters.AddWithValue(
        "$telemetryStatus",
        sample.TelemetryStatus);
    command.Parameters.AddWithValue(
        "$managerInstanceId",
        profile.ManagerInstanceId);
    command.Parameters.AddWithValue(
        "$managerStatus",
        profile.ManagerStatus);
    command.Parameters.AddWithValue("$generation", profile.Generation);
    command.Parameters.AddWithValue("$desiredSlots", profile.DesiredSlots);
    command.Parameters.AddWithValue("$activeSlots", profile.ActiveSlots);
    command.Parameters.AddWithValue("$drainingSlots", profile.DrainingSlots);
    AddNullable(command, "$configuredSlots", profile.ConfiguredSlots);
    AddNullable(command, "$eligibleSlots", profile.EligibleSlots);
    AddNullable(command, "$targetSlots", profile.Autoscaling?.TargetSlots);
    AddNullable(command, "$maximumSlots", profile.Autoscaling?.MaximumSlots);
    AddNullable(command, "$assignedJobs", profile.Autoscaling?.AssignedJobs);
    AddNullable(command, "$runningJobs", profile.Autoscaling?.RunningJobs);
    AddNullable(command, "$availableJobs", profile.Autoscaling?.AvailableJobs);
    AddNullable(command, "$idleRunners", profile.Autoscaling?.IdleRunners);
    AddNullable(command, "$busyRunners", profile.Autoscaling?.BusyRunners);
    command.Parameters.AddWithValue(
        "$localRunningWorkers",
        sample.LocalRunningWorkers);
    AddNullable(
        command,
        "$managerCpuCores",
        profile.ResourceTelemetry?.Manager?.CpuCores);
    AddNullable(
        command,
        "$managerMemoryBytes",
        profile.ResourceTelemetry?.Manager?.MemoryWorkingSetBytes);
    AddNullable(
        command,
        "$managerPids",
        profile.ResourceTelemetry?.Manager?.Pids);
    AddNullable(
        command,
        "$hostLogicalProcessors",
        profile.ResourceTelemetry?.Host?.LogicalProcessorCount);
    AddNullable(
        command,
        "$hostMemoryBytes",
        profile.ResourceTelemetry?.Host?.MemoryBytes);
    AddNullable(command, "$workerCpuCores", sample.WorkerCpuCores);
    AddNullable(command, "$workerMemoryBytes", sample.WorkerMemoryBytes);
    AddNullable(command, "$workerPids", sample.WorkerPids);
    AddNullable(command, "$networkRxBytes", sample.NetworkRxBytes);
    AddNullable(command, "$networkTxBytes", sample.NetworkTxBytes);
    AddNullable(command, "$blockReadBytes", sample.BlockReadBytes);
    AddNullable(command, "$blockWriteBytes", sample.BlockWriteBytes);
    command.Parameters.AddWithValue("$exitReports", sample.ExitReports);
    command.Parameters.AddWithValue(
        "$adverseExitReports",
        sample.AdverseExitReports);
    AddNullable(
        command,
        "$localCapacityDeficit",
        sample.LocalCapacityDeficit);
    AddNullable(
        command,
        "$eligibilityCapacityDeficit",
        sample.EligibilityCapacityDeficit);
    AddNullable(
        command,
        "$capacityDeficitReason",
        sample.CapacityDeficitReason);
    AddNullable(
        command,
        "$capacityDeficitFreshness",
        sample.CapacityDeficitFreshness);
    return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
  }

  private static async Task RecomputeRollupAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      ManagerObservedState profile,
      CancellationToken cancellationToken)
  {
    var bucketStart = new DateTimeOffset(
        profile.ObservedAt.UtcDateTime.Date,
        TimeSpan.Zero).AddHours(profile.ObservedAt.UtcDateTime.Hour);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO profile_telemetry_rollups (
            node_id,
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
            max_local_capacity_deficit)
        SELECT
            $nodeId,
            $profileId,
            $bucketStart,
            COUNT(*),
            MAX(desired_slots),
            MAX(active_slots),
            MAX(draining_slots),
            MAX(eligible_slots),
            MAX(local_running_workers),
            MAX(manager_cpu_cores),
            MAX(manager_memory_bytes),
            MAX(manager_pids),
            MAX(worker_cpu_cores),
            MAX(worker_memory_bytes),
            MAX(worker_pids),
            MAX(network_rx_bytes),
            MAX(network_tx_bytes),
            MAX(block_read_bytes),
            MAX(block_write_bytes),
            MAX(exit_reports),
            MAX(adverse_exit_reports),
            MAX(local_capacity_deficit)
        FROM profile_telemetry_samples
        WHERE node_id = $nodeId
          AND profile_id = $profileId
          AND observed_at >= $bucketStart
          AND observed_at < $bucketEnd
        GROUP BY node_id, profile_id
        ON CONFLICT (node_id, profile_id, bucket_start) DO UPDATE SET
            sample_count = excluded.sample_count,
            max_desired_slots = excluded.max_desired_slots,
            max_active_slots = excluded.max_active_slots,
            max_draining_slots = excluded.max_draining_slots,
            max_eligible_slots = excluded.max_eligible_slots,
            max_local_running_workers = excluded.max_local_running_workers,
            max_manager_cpu_cores = excluded.max_manager_cpu_cores,
            max_manager_memory_bytes = excluded.max_manager_memory_bytes,
            max_manager_pids = excluded.max_manager_pids,
            max_worker_cpu_cores = excluded.max_worker_cpu_cores,
            max_worker_memory_bytes = excluded.max_worker_memory_bytes,
            max_worker_pids = excluded.max_worker_pids,
            max_network_rx_bytes = excluded.max_network_rx_bytes,
            max_network_tx_bytes = excluded.max_network_tx_bytes,
            max_block_read_bytes = excluded.max_block_read_bytes,
            max_block_write_bytes = excluded.max_block_write_bytes,
            max_exit_reports = excluded.max_exit_reports,
            max_adverse_exit_reports = excluded.max_adverse_exit_reports,
            max_local_capacity_deficit = excluded.max_local_capacity_deficit;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profile.ProfileId);
    command.Parameters.AddWithValue("$bucketStart", Utc(bucketStart));
    command.Parameters.AddWithValue(
        "$bucketEnd",
        Utc(bucketStart.AddHours(1)));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task AppendEventsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      ManagerObservedState profile,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken)
  {
    var journal = profile.OperationJournal;
    var events = new Dictionary<long, ManagerEvent>();
    if (journal is not null)
    {
      foreach (var managerEvent in journal.Events)
      {
        events.TryAdd(managerEvent.Sequence, managerEvent);
      }
    }

    long? previousHighest = null;
    await using (var cursorCommand = connection.CreateCommand())
    {
      cursorCommand.Transaction = transaction;
      cursorCommand.CommandText =
          """
          SELECT stored_highest_sequence
          FROM profile_event_cursors
          WHERE node_id = $nodeId
            AND profile_id = $profileId;
          """;
      cursorCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      cursorCommand.Parameters.AddWithValue(
          "$profileId",
          profile.ProfileId);
      var stored = await cursorCommand.ExecuteScalarAsync(cancellationToken);
      if (stored is not null && stored is not DBNull)
      {
        previousHighest = Convert.ToInt64(
            stored,
            CultureInfo.InvariantCulture);
      }
    }

    foreach (var managerEvent in events.Values)
    {
      await using var eventCommand = connection.CreateCommand();
      eventCommand.Transaction = transaction;
      eventCommand.CommandText =
          """
          INSERT INTO profile_manager_events (
              node_id,
              profile_id,
              sequence,
              manager_instance_id,
              observed_at,
              recorded_at,
              subsystem,
              operation,
              target,
              outcome,
              duration_milliseconds,
              attempt,
              consecutive_failures,
              retry_at,
              reason,
              evidence)
          VALUES (
              $nodeId,
              $profileId,
              $sequence,
              $managerInstanceId,
              $observedAt,
              $recordedAt,
              $subsystem,
              $operation,
              $target,
              $outcome,
              $durationMilliseconds,
              $attempt,
              $consecutiveFailures,
              $retryAt,
              $reason,
              $evidence)
          ON CONFLICT (node_id, profile_id, sequence) DO NOTHING;
          """;
      eventCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      eventCommand.Parameters.AddWithValue(
          "$profileId",
          profile.ProfileId);
      eventCommand.Parameters.AddWithValue(
          "$sequence",
          managerEvent.Sequence);
      eventCommand.Parameters.AddWithValue(
          "$managerInstanceId",
          managerEvent.ManagerInstanceId);
      eventCommand.Parameters.AddWithValue(
          "$observedAt",
          Utc(managerEvent.ObservedAt));
      eventCommand.Parameters.AddWithValue(
          "$recordedAt",
          Utc(receivedAt));
      eventCommand.Parameters.AddWithValue(
          "$subsystem",
          managerEvent.Subsystem);
      eventCommand.Parameters.AddWithValue(
          "$operation",
          managerEvent.Operation);
      AddNullable(eventCommand, "$target", managerEvent.Target);
      eventCommand.Parameters.AddWithValue(
          "$outcome",
          managerEvent.Outcome);
      AddNullable(
          eventCommand,
          "$durationMilliseconds",
          managerEvent.DurationMilliseconds);
      AddNullable(eventCommand, "$attempt", managerEvent.Attempt);
      AddNullable(
          eventCommand,
          "$consecutiveFailures",
          managerEvent.ConsecutiveFailures);
      AddNullable(
          eventCommand,
          "$retryAt",
          managerEvent.RetryAt is null
              ? null
              : Utc(managerEvent.RetryAt.Value));
      eventCommand.Parameters.AddWithValue("$reason", managerEvent.Reason);
      AddNullable(eventCommand, "$evidence", managerEvent.Evidence);
      await eventCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    long missed = 0;
    long? highest = previousHighest;
    if (events.Count > 0)
    {
      var lowestDelivered = events.Keys.Min();
      var highestDelivered = events.Keys.Max();
      if (previousHighest is not null &&
          lowestDelivered > previousHighest.Value + 1)
      {
        missed = lowestDelivered - previousHighest.Value - 1;
      }
      highest = previousHighest is null
          ? highestDelivered
          : Math.Max(previousHighest.Value, highestDelivered);
    }

    await using var upsertCommand = connection.CreateCommand();
    upsertCommand.Transaction = transaction;
    upsertCommand.CommandText =
        """
        INSERT INTO profile_event_cursors (
            node_id,
            profile_id,
            journal_status,
            journal_capacity,
            manager_highest_sequence,
            manager_dropped_events,
            stored_highest_sequence,
            missed_events,
            updated_at)
        VALUES (
            $nodeId,
            $profileId,
            $journalStatus,
            $journalCapacity,
            $managerHighestSequence,
            $managerDroppedEvents,
            $storedHighestSequence,
            $missedEvents,
            $updatedAt)
        ON CONFLICT (node_id, profile_id) DO UPDATE SET
            journal_status = excluded.journal_status,
            journal_capacity = excluded.journal_capacity,
            manager_highest_sequence = excluded.manager_highest_sequence,
            manager_dropped_events = excluded.manager_dropped_events,
            stored_highest_sequence = excluded.stored_highest_sequence,
            missed_events =
                profile_event_cursors.missed_events + $missedEvents,
            updated_at = excluded.updated_at;
        """;
    upsertCommand.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    upsertCommand.Parameters.AddWithValue("$profileId", profile.ProfileId);
    upsertCommand.Parameters.AddWithValue(
        "$journalStatus",
        journal?.Status ?? "unreported");
    upsertCommand.Parameters.AddWithValue(
        "$journalCapacity",
        journal?.Capacity ?? 0);
    AddNullable(
        upsertCommand,
        "$managerHighestSequence",
        journal?.HighestSequence);
    upsertCommand.Parameters.AddWithValue(
        "$managerDroppedEvents",
        journal?.DroppedEvents ?? 0);
    AddNullable(upsertCommand, "$storedHighestSequence", highest);
    upsertCommand.Parameters.AddWithValue("$missedEvents", missed);
    upsertCommand.Parameters.AddWithValue("$updatedAt", Utc(receivedAt));
    await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task ApplyRetentionAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string profileId,
      DateTimeOffset receivedAt,
      HistoryRetentionPolicy retention,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        DELETE FROM profile_telemetry_samples
        WHERE node_id = $nodeId
          AND profile_id = $profileId
          AND (observed_at < $sampleCutoff
            OR observed_at NOT IN (
                SELECT observed_at
                FROM profile_telemetry_samples
                WHERE node_id = $nodeId
                  AND profile_id = $profileId
                ORDER BY observed_at DESC
                LIMIT $maximumSamples));

        DELETE FROM profile_telemetry_rollups
        WHERE node_id = $nodeId
          AND profile_id = $profileId
          AND bucket_start < $rollupCutoff;

        DELETE FROM profile_manager_events
        WHERE node_id = $nodeId
          AND profile_id = $profileId
          AND (observed_at < $eventCutoff
            OR sequence NOT IN (
                SELECT sequence
                FROM profile_manager_events
                WHERE node_id = $nodeId
                  AND profile_id = $profileId
                ORDER BY sequence DESC
                LIMIT $maximumEvents));
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profileId);
    command.Parameters.AddWithValue(
        "$sampleCutoff",
        Utc(receivedAt - retention.SampleRetention));
    command.Parameters.AddWithValue(
        "$rollupCutoff",
        Utc(receivedAt - retention.RollupRetention));
    command.Parameters.AddWithValue(
        "$eventCutoff",
        Utc(receivedAt - retention.EventRetention));
    command.Parameters.AddWithValue(
        "$maximumSamples",
        retention.MaximumSamplesPerProfile);
    command.Parameters.AddWithValue(
        "$maximumEvents",
        retention.MaximumEventsPerProfile);
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
    await using (var ownershipCommand = connection.CreateCommand())
    {
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
            nodeId,
            profileId,
            window,
            cancellationToken)
        : [];
    var rollups = window.Resolution == HistoryResolution.Hourly
        ? await LoadRollupsAsync(
            connection,
            nodeId,
            profileId,
            window,
            cancellationToken)
        : [];
    var events = await LoadEventsAsync(
        connection,
        nodeId,
        profileId,
        window,
        cancellationToken);
    var journals = await LoadJournalsAsync(
        connection,
        nodeId,
        profileId,
        cancellationToken);

    var profileIds = new SortedSet<string>(StringComparer.Ordinal);
    profileIds.UnionWith(journals.Keys);
    profileIds.UnionWith(samples.Keys);
    profileIds.UnionWith(rollups.Keys);
    profileIds.UnionWith(events.Keys);

    var histories = new List<ProfileHistory>(profileIds.Count);
    foreach (var id in profileIds)
    {
      var samplePage = samples.GetValueOrDefault(id);
      var rollupPage = rollups.GetValueOrDefault(id);
      var eventPage = events.GetValueOrDefault(id);
      histories.Add(new ProfileHistory(
          id,
          samplePage?.Samples ?? [],
          rollupPage?.Rollups ?? [],
          eventPage?.Events ?? [],
          (samplePage?.Truncated ?? false) || (rollupPage?.Truncated ?? false),
          eventPage?.Truncated ?? false,
          journals.GetValueOrDefault(id) ?? EmptyJournal()));
    }

    return new NodeHistoryResponse(
        nodeId,
        generatedAt,
        window.From,
        window.To,
        window.Resolution == HistoryResolution.Hourly ? "hourly" : "raw",
        histories);
  }

  private static async Task<Dictionary<string, SamplePage>> LoadSamplesAsync(
      SqliteConnection connection,
      Guid nodeId,
      string? profileId,
      HistoryWindow window,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.CommandText =
        $"""
        SELECT
            profile_id,
            {SampleColumns},
            total_points
        FROM (
            SELECT
                profile_id,
                {SampleColumns},
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY observed_at DESC) AS row_index,
                COUNT(*) OVER (PARTITION BY profile_id) AS total_points
            FROM profile_telemetry_samples
            WHERE node_id = $nodeId
              AND observed_at >= $from
              AND observed_at < $to
              AND ($profileId IS NULL OR profile_id = $profileId))
        WHERE row_index <= $pointLimit
        ORDER BY profile_id, observed_at;
        """;
    AddWindowParameters(command, nodeId, profileId, window);
    var pages = new Dictionary<string, SamplePage>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      var id = reader.GetString(0);
      if (!pages.TryGetValue(id, out var page))
      {
        page = new SamplePage(
            [],
            reader.GetInt64(38) > window.PointLimit);
        pages[id] = page;
      }

      page.Samples.Add(new ProfileTelemetrySample(
          ReadTime(reader, 1),
          ReadOptionalTime(reader, 2),
          reader.GetString(3),
          reader.GetString(4),
          reader.GetString(5),
          reader.GetInt32(6),
          reader.GetInt32(7),
          reader.GetInt32(8),
          reader.GetInt32(9),
          ReadOptionalInt32(reader, 10),
          ReadOptionalInt32(reader, 11),
          ReadOptionalInt32(reader, 12),
          ReadOptionalInt32(reader, 13),
          ReadOptionalInt32(reader, 14),
          ReadOptionalInt32(reader, 15),
          ReadOptionalInt32(reader, 16),
          ReadOptionalInt32(reader, 17),
          ReadOptionalInt32(reader, 18),
          reader.GetInt32(19),
          ReadOptionalDouble(reader, 20),
          ReadOptionalInt64(reader, 21),
          ReadOptionalInt32(reader, 22),
          ReadOptionalInt32(reader, 23),
          ReadOptionalInt64(reader, 24),
          ReadOptionalDouble(reader, 25),
          ReadOptionalInt64(reader, 26),
          ReadOptionalInt32(reader, 27),
          ReadOptionalInt64(reader, 28),
          ReadOptionalInt64(reader, 29),
          ReadOptionalInt64(reader, 30),
          ReadOptionalInt64(reader, 31),
          reader.GetInt32(32),
          reader.GetInt32(33),
          ReadOptionalInt32(reader, 34),
          ReadOptionalInt32(reader, 35),
          ReadOptionalString(reader, 36),
          ReadOptionalString(reader, 37)));
    }

    return pages;
  }

  private static async Task<Dictionary<string, RollupPage>> LoadRollupsAsync(
      SqliteConnection connection,
      Guid nodeId,
      string? profileId,
      HistoryWindow window,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
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
            total_points
        FROM (
            SELECT
                *,
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY bucket_start DESC) AS row_index,
                COUNT(*) OVER (PARTITION BY profile_id) AS total_points
            FROM profile_telemetry_rollups
            WHERE node_id = $nodeId
              AND bucket_start >= $from
              AND bucket_start < $to
              AND ($profileId IS NULL OR profile_id = $profileId))
        WHERE row_index <= $pointLimit
        ORDER BY profile_id, bucket_start;
        """;
    AddWindowParameters(command, nodeId, profileId, window);
    var pages = new Dictionary<string, RollupPage>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      var id = reader.GetString(0);
      if (!pages.TryGetValue(id, out var page))
      {
        page = new RollupPage(
            [],
            reader.GetInt64(21) > window.PointLimit);
        pages[id] = page;
      }

      page.Rollups.Add(new ProfileTelemetryRollup(
          ReadTime(reader, 1),
          reader.GetInt32(2),
          reader.GetInt32(3),
          reader.GetInt32(4),
          reader.GetInt32(5),
          ReadOptionalInt32(reader, 6),
          reader.GetInt32(7),
          ReadOptionalDouble(reader, 8),
          ReadOptionalInt64(reader, 9),
          ReadOptionalInt32(reader, 10),
          ReadOptionalDouble(reader, 11),
          ReadOptionalInt64(reader, 12),
          ReadOptionalInt32(reader, 13),
          ReadOptionalInt64(reader, 14),
          ReadOptionalInt64(reader, 15),
          ReadOptionalInt64(reader, 16),
          ReadOptionalInt64(reader, 17),
          reader.GetInt32(18),
          reader.GetInt32(19),
          ReadOptionalInt32(reader, 20)));
    }

    return pages;
  }

  private static async Task<Dictionary<string, EventPage>> LoadEventsAsync(
      SqliteConnection connection,
      Guid nodeId,
      string? profileId,
      HistoryWindow window,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
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
            total_points
        FROM (
            SELECT
                *,
                ROW_NUMBER() OVER (
                    PARTITION BY profile_id
                    ORDER BY sequence DESC) AS row_index,
                COUNT(*) OVER (PARTITION BY profile_id) AS total_points
            FROM profile_manager_events
            WHERE node_id = $nodeId
              AND observed_at >= $from
              AND observed_at < $to
              AND ($profileId IS NULL OR profile_id = $profileId))
        WHERE row_index <= $eventLimit
        ORDER BY profile_id, sequence DESC;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$profileId",
        (object?)profileId ?? DBNull.Value);
    command.Parameters.AddWithValue("$from", Utc(window.From));
    command.Parameters.AddWithValue("$to", Utc(window.To));
    command.Parameters.AddWithValue("$eventLimit", window.EventLimit);
    var pages = new Dictionary<string, EventPage>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      var id = reader.GetString(0);
      if (!pages.TryGetValue(id, out var page))
      {
        page = new EventPage(
            [],
            reader.GetInt64(14) > window.EventLimit);
        pages[id] = page;
      }

      page.Events.Add(new ManagerEvent(
          reader.GetInt64(1),
          reader.GetString(2),
          ReadTime(reader, 3),
          reader.GetString(4),
          reader.GetString(5),
          ReadOptionalString(reader, 6),
          reader.GetString(7),
          ReadOptionalInt32(reader, 8),
          ReadOptionalInt32(reader, 9),
          ReadOptionalInt32(reader, 10),
          ReadOptionalTime(reader, 11),
          reader.GetString(12),
          ReadOptionalString(reader, 13)));
    }

    return pages;
  }

  private static async Task<Dictionary<string, ProfileEventJournalState>>
      LoadJournalsAsync(
          SqliteConnection connection,
          Guid nodeId,
          string? profileId,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT
            c.profile_id,
            c.journal_status,
            c.journal_capacity,
            c.manager_highest_sequence,
            c.manager_dropped_events,
            c.stored_highest_sequence,
            c.missed_events,
            c.updated_at,
            (SELECT MIN(e.sequence)
             FROM profile_manager_events AS e
             WHERE e.node_id = c.node_id
               AND e.profile_id = c.profile_id)
        FROM profile_event_cursors AS c
        WHERE c.node_id = $nodeId
          AND ($profileId IS NULL OR c.profile_id = $profileId);
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$profileId",
        (object?)profileId ?? DBNull.Value);
    var journals = new Dictionary<string, ProfileEventJournalState>(
        StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      var managerHighest = ReadOptionalInt64(reader, 3);
      var storedHighest = ReadOptionalInt64(reader, 5);
      var undelivered = managerHighest is null || storedHighest is null
          ? 0
          : Math.Max(0, managerHighest.Value - storedHighest.Value);
      journals[reader.GetString(0)] = new ProfileEventJournalState(
          reader.GetString(1),
          reader.GetInt32(2),
          managerHighest,
          ReadOptionalInt64(reader, 8),
          storedHighest,
          reader.GetInt32(4),
          reader.GetInt64(6),
          undelivered,
          ReadTime(reader, 7));
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
          null);

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

  private static string Utc(DateTimeOffset value) =>
      value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

  private static void AddNullable(
      SqliteCommand command,
      string name,
      object? value) =>
      command.Parameters.AddWithValue(name, value ?? DBNull.Value);

  private static DateTimeOffset ReadTime(SqliteDataReader reader, int index) =>
      DateTimeOffset.Parse(
          reader.GetString(index),
          CultureInfo.InvariantCulture,
          DateTimeStyles.RoundtripKind);

  private static DateTimeOffset? ReadOptionalTime(
      SqliteDataReader reader,
      int index) =>
      reader.IsDBNull(index)
          ? null
          : ReadTime(reader, index);

  private static int? ReadOptionalInt32(SqliteDataReader reader, int index) =>
      reader.IsDBNull(index)
          ? null
          : reader.GetInt32(index);

  private static long? ReadOptionalInt64(SqliteDataReader reader, int index) =>
      reader.IsDBNull(index)
          ? null
          : reader.GetInt64(index);

  private static double? ReadOptionalDouble(
      SqliteDataReader reader,
      int index) =>
      reader.IsDBNull(index)
          ? null
          : reader.GetDouble(index);

  private static string? ReadOptionalString(
      SqliteDataReader reader,
      int index) =>
      reader.IsDBNull(index)
          ? null
          : reader.GetString(index);

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
}
