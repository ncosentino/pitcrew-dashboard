using System.Globalization;

using Microsoft.Data.Sqlite;
using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed partial class SqliteFleetHistoryStore(
    SqliteConnectionFactory _connectionFactory) : IFleetHistoryStore
{
  private const string FixedTargetKey = "fixed";

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
      IFleetStorageTransaction transaction,
      Guid nodeId,
      IReadOnlyList<ManagerObservedState> profiles,
      DateTimeOffset receivedAt,
      HistoryAppendPolicy policy,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(profiles);
    ArgumentNullException.ThrowIfNull(policy);
    var enlisted = SqliteFleetTransaction.Resolve(transaction);
    var connection = enlisted.Connection;
    var sqliteTransaction = enlisted.Transaction;
    var horizon = receivedAt + policy.MaximumClockSkew;
    foreach (var profile in profiles)
    {
      await EnsureCursorAsync(
          connection,
          sqliteTransaction,
          nodeId,
          profile.ProfileId,
          receivedAt,
          cancellationToken);
      if (profile.ObservedAt > horizon)
      {
        await CountRejectedObservationAsync(
            connection,
            sqliteTransaction,
            nodeId,
            profile.ProfileId,
            receivedAt,
            cancellationToken);
      }
      else
      {
        var appended = await AppendSampleAsync(
            connection,
            sqliteTransaction,
            nodeId,
            profile,
            receivedAt,
            cancellationToken);
        if (appended)
        {
          await AccumulateRollupAsync(
              connection,
              sqliteTransaction,
              nodeId,
              profile,
              cancellationToken);
          await AppendSubsystemHealthAsync(
              connection,
              sqliteTransaction,
              nodeId,
              profile,
              receivedAt,
              horizon,
              cancellationToken);
          await AppendCapacityDeficitsAsync(
              connection,
              sqliteTransaction,
              nodeId,
              profile,
              receivedAt,
              horizon,
              cancellationToken);
        }
      }

      await AppendEventsAsync(
          connection,
          sqliteTransaction,
          nodeId,
          profile,
          receivedAt,
          horizon,
          cancellationToken);
    }

    await ApplyRetentionAsync(
        connection,
        sqliteTransaction,
        nodeId,
        receivedAt,
        policy.Retention,
        cancellationToken);
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

  private static async Task EnsureCursorAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string profileId,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO profile_history_cursors (
            node_id,
            profile_id,
            journal_status,
            journal_capacity,
            epoch,
            epoch_resets,
            manager_highest_sequence,
            manager_dropped_events,
            stored_highest_sequence,
            missed_events,
            dropped_samples,
            dropped_rollups,
            dropped_events,
            dropped_subsystem_health,
            dropped_capacity_deficits,
            rejected_future_samples,
            rejected_future_events,
            updated_at)
        VALUES (
            $nodeId,
            $profileId,
            'unreported',
            0,
            0,
            0,
            NULL,
            0,
            NULL,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            $updatedAt)
        ON CONFLICT (node_id, profile_id) DO NOTHING;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profileId);
    command.Parameters.AddWithValue("$updatedAt", Utc(receivedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task CountRejectedObservationAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string profileId,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        UPDATE profile_history_cursors
        SET rejected_future_samples = rejected_future_samples + 1,
            updated_at = $updatedAt
        WHERE node_id = $nodeId
          AND profile_id = $profileId;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profileId);
    command.Parameters.AddWithValue("$updatedAt", Utc(receivedAt));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

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

  private static async Task AccumulateRollupAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      ManagerObservedState profile,
      CancellationToken cancellationToken)
  {
    var bucketStart = BucketStart(profile.ObservedAt);
    var sample = ProjectSample(profile);
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
            max_local_capacity_deficit,
            max_eligibility_capacity_deficit,
            max_target_slots,
            max_assigned_jobs,
            max_idle_runners,
            max_busy_runners)
        VALUES (
            $nodeId,
            $profileId,
            $bucketStart,
            1,
            $desiredSlots,
            $activeSlots,
            $drainingSlots,
            $eligibleSlots,
            $localRunningWorkers,
            $managerCpuCores,
            $managerMemoryBytes,
            $managerPids,
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
            $targetSlots,
            $assignedJobs,
            $idleRunners,
            $busyRunners)
        ON CONFLICT (node_id, profile_id, bucket_start) DO UPDATE SET
            sample_count = sample_count + 1,
            max_desired_slots = MAX(max_desired_slots, excluded.max_desired_slots),
            max_active_slots = MAX(max_active_slots, excluded.max_active_slots),
            max_draining_slots = MAX(max_draining_slots, excluded.max_draining_slots),
            max_eligible_slots = MAX(
                COALESCE(max_eligible_slots, excluded.max_eligible_slots),
                COALESCE(excluded.max_eligible_slots, max_eligible_slots)),
            max_local_running_workers = MAX(
                max_local_running_workers,
                excluded.max_local_running_workers),
            max_manager_cpu_cores = MAX(
                COALESCE(max_manager_cpu_cores, excluded.max_manager_cpu_cores),
                COALESCE(excluded.max_manager_cpu_cores, max_manager_cpu_cores)),
            max_manager_memory_bytes = MAX(
                COALESCE(max_manager_memory_bytes, excluded.max_manager_memory_bytes),
                COALESCE(excluded.max_manager_memory_bytes, max_manager_memory_bytes)),
            max_manager_pids = MAX(
                COALESCE(max_manager_pids, excluded.max_manager_pids),
                COALESCE(excluded.max_manager_pids, max_manager_pids)),
            max_worker_cpu_cores = MAX(
                COALESCE(max_worker_cpu_cores, excluded.max_worker_cpu_cores),
                COALESCE(excluded.max_worker_cpu_cores, max_worker_cpu_cores)),
            max_worker_memory_bytes = MAX(
                COALESCE(max_worker_memory_bytes, excluded.max_worker_memory_bytes),
                COALESCE(excluded.max_worker_memory_bytes, max_worker_memory_bytes)),
            max_worker_pids = MAX(
                COALESCE(max_worker_pids, excluded.max_worker_pids),
                COALESCE(excluded.max_worker_pids, max_worker_pids)),
            max_network_rx_bytes = MAX(
                COALESCE(max_network_rx_bytes, excluded.max_network_rx_bytes),
                COALESCE(excluded.max_network_rx_bytes, max_network_rx_bytes)),
            max_network_tx_bytes = MAX(
                COALESCE(max_network_tx_bytes, excluded.max_network_tx_bytes),
                COALESCE(excluded.max_network_tx_bytes, max_network_tx_bytes)),
            max_block_read_bytes = MAX(
                COALESCE(max_block_read_bytes, excluded.max_block_read_bytes),
                COALESCE(excluded.max_block_read_bytes, max_block_read_bytes)),
            max_block_write_bytes = MAX(
                COALESCE(max_block_write_bytes, excluded.max_block_write_bytes),
                COALESCE(excluded.max_block_write_bytes, max_block_write_bytes)),
            max_exit_reports = MAX(max_exit_reports, excluded.max_exit_reports),
            max_adverse_exit_reports = MAX(
                max_adverse_exit_reports,
                excluded.max_adverse_exit_reports),
            max_local_capacity_deficit = MAX(
                COALESCE(max_local_capacity_deficit, excluded.max_local_capacity_deficit),
                COALESCE(excluded.max_local_capacity_deficit, max_local_capacity_deficit)),
            max_eligibility_capacity_deficit = MAX(
                COALESCE(
                    max_eligibility_capacity_deficit,
                    excluded.max_eligibility_capacity_deficit),
                COALESCE(
                    excluded.max_eligibility_capacity_deficit,
                    max_eligibility_capacity_deficit)),
            max_target_slots = MAX(
                COALESCE(max_target_slots, excluded.max_target_slots),
                COALESCE(excluded.max_target_slots, max_target_slots)),
            max_assigned_jobs = MAX(
                COALESCE(max_assigned_jobs, excluded.max_assigned_jobs),
                COALESCE(excluded.max_assigned_jobs, max_assigned_jobs)),
            max_idle_runners = MAX(
                COALESCE(max_idle_runners, excluded.max_idle_runners),
                COALESCE(excluded.max_idle_runners, max_idle_runners)),
            max_busy_runners = MAX(
                COALESCE(max_busy_runners, excluded.max_busy_runners),
                COALESCE(excluded.max_busy_runners, max_busy_runners));
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profile.ProfileId);
    command.Parameters.AddWithValue("$bucketStart", Utc(bucketStart));
    command.Parameters.AddWithValue("$desiredSlots", profile.DesiredSlots);
    command.Parameters.AddWithValue("$activeSlots", profile.ActiveSlots);
    command.Parameters.AddWithValue("$drainingSlots", profile.DrainingSlots);
    AddNullable(command, "$eligibleSlots", profile.EligibleSlots);
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
    AddNullable(command, "$targetSlots", profile.Autoscaling?.TargetSlots);
    AddNullable(command, "$assignedJobs", profile.Autoscaling?.AssignedJobs);
    AddNullable(command, "$idleRunners", profile.Autoscaling?.IdleRunners);
    AddNullable(command, "$busyRunners", profile.Autoscaling?.BusyRunners);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task AppendSubsystemHealthAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      ManagerObservedState profile,
      DateTimeOffset receivedAt,
      DateTimeOffset horizon,
      CancellationToken cancellationToken)
  {
    var health = profile.SubsystemHealth;
    if (health is null)
    {
      return;
    }

    await AppendSubsystemAsync(
        connection,
        transaction,
        nodeId,
        profile.ProfileId,
        "docker",
        health.Docker,
        receivedAt,
        horizon,
        cancellationToken);
    await AppendSubsystemAsync(
        connection,
        transaction,
        nodeId,
        profile.ProfileId,
        "github",
        health.Github,
        receivedAt,
        horizon,
        cancellationToken);
  }

  private static async Task AppendSubsystemAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string profileId,
      string subsystem,
      SubsystemHealthSummary summary,
      DateTimeOffset receivedAt,
      DateTimeOffset horizon,
      CancellationToken cancellationToken)
  {
    if (summary is null || summary.ObservedAt > horizon)
    {
      return;
    }

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO profile_subsystem_health (
            node_id,
            profile_id,
            subsystem,
            observed_at,
            recorded_at,
            state,
            consecutive_failures,
            retry_at,
            last_success_operation,
            last_success_observed_at,
            last_success_reason,
            last_failure_operation,
            last_failure_observed_at,
            last_failure_reason,
            last_failure_evidence)
        SELECT
            $nodeId,
            $profileId,
            $subsystem,
            $observedAt,
            $recordedAt,
            $state,
            $consecutiveFailures,
            $retryAt,
            $lastSuccessOperation,
            $lastSuccessObservedAt,
            $lastSuccessReason,
            $lastFailureOperation,
            $lastFailureObservedAt,
            $lastFailureReason,
            $lastFailureEvidence
        WHERE NOT EXISTS (
            SELECT 1
            FROM profile_subsystem_health AS existing
            WHERE existing.node_id = $nodeId
              AND existing.profile_id = $profileId
              AND existing.subsystem = $subsystem
              AND existing.observed_at = (
                  SELECT MAX(latest.observed_at)
                  FROM profile_subsystem_health AS latest
                  WHERE latest.node_id = $nodeId
                    AND latest.profile_id = $profileId
                    AND latest.subsystem = $subsystem)
              AND existing.state IS $state
              AND existing.consecutive_failures IS $consecutiveFailures
              AND existing.retry_at IS $retryAt
              AND existing.last_success_operation IS $lastSuccessOperation
              AND existing.last_success_observed_at IS $lastSuccessObservedAt
              AND existing.last_success_reason IS $lastSuccessReason
              AND existing.last_failure_operation IS $lastFailureOperation
              AND existing.last_failure_observed_at IS $lastFailureObservedAt
              AND existing.last_failure_reason IS $lastFailureReason
              AND existing.last_failure_evidence IS $lastFailureEvidence);
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profileId);
    command.Parameters.AddWithValue("$subsystem", subsystem);
    command.Parameters.AddWithValue("$observedAt", Utc(summary.ObservedAt));
    command.Parameters.AddWithValue("$recordedAt", Utc(receivedAt));
    command.Parameters.AddWithValue("$state", summary.State);
    command.Parameters.AddWithValue(
        "$consecutiveFailures",
        summary.ConsecutiveFailures);
    AddNullable(
        command,
        "$retryAt",
        summary.RetryAt is null ? null : Utc(summary.RetryAt.Value));
    AddNullable(
        command,
        "$lastSuccessOperation",
        summary.LastSuccess?.Operation);
    AddNullable(
        command,
        "$lastSuccessObservedAt",
        summary.LastSuccess is null
            ? null
            : Utc(summary.LastSuccess.ObservedAt));
    AddNullable(command, "$lastSuccessReason", summary.LastSuccess?.Reason);
    AddNullable(
        command,
        "$lastFailureOperation",
        summary.LastFailure?.Operation);
    AddNullable(
        command,
        "$lastFailureObservedAt",
        summary.LastFailure is null
            ? null
            : Utc(summary.LastFailure.ObservedAt));
    AddNullable(command, "$lastFailureReason", summary.LastFailure?.Reason);
    AddNullable(command, "$lastFailureEvidence", summary.LastFailure?.Evidence);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task AppendCapacityDeficitsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      ManagerObservedState profile,
      DateTimeOffset receivedAt,
      DateTimeOffset horizon,
      CancellationToken cancellationToken)
  {
    var evidence = profile.CapacityEvidence;
    if (evidence is null)
    {
      return;
    }

    if (evidence.Fixed is not null)
    {
      await AppendCapacityDeficitAsync(
          connection,
          transaction,
          nodeId,
          profile.ProfileId,
          FixedTargetKey,
          null,
          evidence.Fixed,
          receivedAt,
          horizon,
          cancellationToken);
    }

    foreach (var target in evidence.Targets)
    {
      await AppendCapacityDeficitAsync(
          connection,
          transaction,
          nodeId,
          profile.ProfileId,
          target.Key,
          target.Repository,
          target,
          receivedAt,
          horizon,
          cancellationToken);
    }
  }

  private static async Task AppendCapacityDeficitAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string profileId,
      string targetKey,
      string? repository,
      CapacityDeficitEvidence evidence,
      DateTimeOffset receivedAt,
      DateTimeOffset horizon,
      CancellationToken cancellationToken)
  {
    if (evidence.ObservedAt > horizon)
    {
      return;
    }

    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        INSERT INTO profile_capacity_deficits (
            node_id,
            profile_id,
            target_key,
            observed_at,
            recorded_at,
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
            evidence)
        SELECT
            $nodeId,
            $profileId,
            $targetKey,
            $observedAt,
            $recordedAt,
            $repository,
            $freshness,
            $targetSlots,
            $activeWorkers,
            $startingWorkers,
            $drainingWorkers,
            $cleanupPendingWorkers,
            $eligibleWorkers,
            $localDeficit,
            $eligibilityDeficit,
            $reason,
            $evidence
        WHERE NOT EXISTS (
            SELECT 1
            FROM profile_capacity_deficits AS existing
            WHERE existing.node_id = $nodeId
              AND existing.profile_id = $profileId
              AND existing.target_key = $targetKey
              AND existing.observed_at = (
                  SELECT MAX(latest.observed_at)
                  FROM profile_capacity_deficits AS latest
                  WHERE latest.node_id = $nodeId
                    AND latest.profile_id = $profileId
                    AND latest.target_key = $targetKey)
              AND existing.repository IS $repository
              AND existing.freshness IS $freshness
              AND existing.target_slots IS $targetSlots
              AND existing.active_workers IS $activeWorkers
              AND existing.starting_workers IS $startingWorkers
              AND existing.draining_workers IS $drainingWorkers
              AND existing.cleanup_pending_workers IS $cleanupPendingWorkers
              AND existing.eligible_workers IS $eligibleWorkers
              AND existing.local_deficit IS $localDeficit
              AND existing.eligibility_deficit IS $eligibilityDeficit
              AND existing.reason IS $reason
              AND existing.evidence IS $evidence);
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profileId);
    command.Parameters.AddWithValue("$targetKey", targetKey);
    command.Parameters.AddWithValue("$observedAt", Utc(evidence.ObservedAt));
    command.Parameters.AddWithValue("$recordedAt", Utc(receivedAt));
    AddNullable(command, "$repository", repository);
    command.Parameters.AddWithValue("$freshness", evidence.Freshness);
    command.Parameters.AddWithValue("$targetSlots", evidence.TargetSlots);
    command.Parameters.AddWithValue("$activeWorkers", evidence.ActiveWorkers);
    command.Parameters.AddWithValue(
        "$startingWorkers",
        evidence.StartingWorkers);
    command.Parameters.AddWithValue(
        "$drainingWorkers",
        evidence.DrainingWorkers);
    command.Parameters.AddWithValue(
        "$cleanupPendingWorkers",
        evidence.CleanupPendingWorkers);
    AddNullable(command, "$eligibleWorkers", evidence.EligibleWorkers);
    command.Parameters.AddWithValue("$localDeficit", evidence.LocalDeficit);
    AddNullable(command, "$eligibilityDeficit", evidence.EligibilityDeficit);
    command.Parameters.AddWithValue("$reason", evidence.Reason);
    AddNullable(command, "$evidence", evidence.Evidence);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task AppendEventsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      ManagerObservedState profile,
      DateTimeOffset receivedAt,
      DateTimeOffset horizon,
      CancellationToken cancellationToken)
  {
    var journal = profile.OperationJournal;
    var events = new Dictionary<long, ManagerEvent>();
    long rejectedFutureEvents = 0;
    if (journal is not null)
    {
      foreach (var managerEvent in journal.Events)
      {
        if (managerEvent.ObservedAt > horizon)
        {
          rejectedFutureEvents++;
          continue;
        }

        events.TryAdd(managerEvent.Sequence, managerEvent);
      }
    }

    var cursor = await ReadCursorAsync(
        connection,
        transaction,
        nodeId,
        profile.ProfileId,
        cancellationToken);
    long? deliveredHighest = events.Count == 0
        ? null
        : events.Keys.Max();
    var effectiveHighest = journal?.HighestSequence ?? deliveredHighest;
    var epoch = cursor.Epoch;
    var epochResets = cursor.EpochResets;
    var previousHighest = cursor.StoredHighestSequence;
    var isReset = previousHighest is not null &&
        effectiveHighest is not null &&
        effectiveHighest.Value < previousHighest.Value;
    if (!isReset && events.Count > 0)
    {
      isReset = await ConflictsWithStoredEpochAsync(
          connection,
          transaction,
          nodeId,
          profile.ProfileId,
          epoch,
          events,
          cancellationToken);
    }

    if (isReset)
    {
      epoch++;
      epochResets++;
      previousHighest = null;
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
              epoch,
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
              $epoch,
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
          ON CONFLICT (node_id, profile_id, epoch, sequence) DO NOTHING;
          """;
      eventCommand.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      eventCommand.Parameters.AddWithValue(
          "$profileId",
          profile.ProfileId);
      eventCommand.Parameters.AddWithValue("$epoch", epoch);
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
    var highest = previousHighest;
    if (events.Count > 0)
    {
      var lowestDelivered = events.Keys.Min();
      if (previousHighest is not null &&
          lowestDelivered > previousHighest.Value + 1)
      {
        missed = lowestDelivered - previousHighest.Value - 1;
      }
      highest = previousHighest is null
          ? deliveredHighest
          : Math.Max(previousHighest.Value, deliveredHighest!.Value);
    }

    await using var cursorCommand = connection.CreateCommand();
    cursorCommand.Transaction = transaction;
    cursorCommand.CommandText =
        """
        UPDATE profile_history_cursors
        SET journal_status = $journalStatus,
            journal_capacity = $journalCapacity,
            epoch = $epoch,
            epoch_resets = $epochResets,
            manager_highest_sequence = $managerHighestSequence,
            manager_dropped_events = $managerDroppedEvents,
            stored_highest_sequence = $storedHighestSequence,
            missed_events = missed_events + $missedEvents,
            rejected_future_events =
                rejected_future_events + $rejectedFutureEvents,
            updated_at = $updatedAt
        WHERE node_id = $nodeId
          AND profile_id = $profileId;
        """;
    cursorCommand.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    cursorCommand.Parameters.AddWithValue("$profileId", profile.ProfileId);
    cursorCommand.Parameters.AddWithValue(
        "$journalStatus",
        journal?.Status ?? "unreported");
    cursorCommand.Parameters.AddWithValue(
        "$journalCapacity",
        journal?.Capacity ?? 0);
    cursorCommand.Parameters.AddWithValue("$epoch", epoch);
    cursorCommand.Parameters.AddWithValue("$epochResets", epochResets);
    AddNullable(
        cursorCommand,
        "$managerHighestSequence",
        journal?.HighestSequence);
    cursorCommand.Parameters.AddWithValue(
        "$managerDroppedEvents",
        journal?.DroppedEvents ?? 0);
    AddNullable(cursorCommand, "$storedHighestSequence", highest);
    cursorCommand.Parameters.AddWithValue("$missedEvents", missed);
    cursorCommand.Parameters.AddWithValue(
        "$rejectedFutureEvents",
        rejectedFutureEvents);
    cursorCommand.Parameters.AddWithValue("$updatedAt", Utc(receivedAt));
    await cursorCommand.ExecuteNonQueryAsync(cancellationToken);
  }

  /// <summary>
  /// Detects a manager journal reset that reuses sequences the current epoch already retained.
  /// </summary>
  /// <remarks>
  /// A reset manager can restart its journal and reach the same or a higher high-water mark before
  /// the next heartbeat, so a sequence comparison alone cannot see it. An incoming sequence that
  /// already exists in the current epoch with different manager identity or different recorded
  /// content proves the reuse is a new journal generation rather than a replay; an identical replay
  /// is deduplicated instead.
  /// </remarks>
  private static async Task<bool> ConflictsWithStoredEpochAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string profileId,
      long epoch,
      Dictionary<long, ManagerEvent> events,
      CancellationToken cancellationToken)
  {
    var sequences = events.Keys.ToList();
    var placeholders = string.Join(
        ", ",
        sequences.Select((_, index) => $"$sequence{index}"));
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        $"""
        SELECT
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
        FROM profile_manager_events
        WHERE node_id = $nodeId
          AND profile_id = $profileId
          AND epoch = $epoch
          AND sequence IN ({placeholders});
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profileId);
    command.Parameters.AddWithValue("$epoch", epoch);
    for (var index = 0; index < sequences.Count; index++)
    {
      command.Parameters.AddWithValue($"$sequence{index}", sequences[index]);
    }

    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    SqliteRowReader? row = null;
    while (await reader.ReadAsync(cancellationToken))
    {
      row ??= new SqliteRowReader(reader);
      var stored = events[row.Int64("sequence")];
      if (!IsSameEvent(row, stored))
      {
        return true;
      }
    }

    return false;
  }

  private static bool IsSameEvent(SqliteRowReader row, ManagerEvent stored) =>
      string.Equals(
          row.String("manager_instance_id"),
          stored.ManagerInstanceId,
          StringComparison.Ordinal) &&
      row.Time("observed_at") == stored.ObservedAt &&
      string.Equals(
          row.String("subsystem"),
          stored.Subsystem,
          StringComparison.Ordinal) &&
      string.Equals(
          row.String("operation"),
          stored.Operation,
          StringComparison.Ordinal) &&
      string.Equals(
          row.OptionalString("target"),
          stored.Target,
          StringComparison.Ordinal) &&
      string.Equals(
          row.String("outcome"),
          stored.Outcome,
          StringComparison.Ordinal) &&
      row.OptionalInt32("duration_milliseconds") ==
          stored.DurationMilliseconds &&
      row.OptionalInt32("attempt") == stored.Attempt &&
      row.OptionalInt32("consecutive_failures") == stored.ConsecutiveFailures &&
      row.OptionalTime("retry_at") == stored.RetryAt &&
      string.Equals(
          row.String("reason"),
          stored.Reason,
          StringComparison.Ordinal) &&
      string.Equals(
          row.OptionalString("evidence"),
          stored.Evidence,
          StringComparison.Ordinal);

  private static async Task<CursorState> ReadCursorAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      Guid nodeId,
      string profileId,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            epoch,
            epoch_resets,
            stored_highest_sequence
        FROM profile_history_cursors
        WHERE node_id = $nodeId
          AND profile_id = $profileId;
        """;
    command.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
    command.Parameters.AddWithValue("$profileId", profileId);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
      return new CursorState(0, 0, null);
    }

    var row = new SqliteRowReader(reader);
    return new CursorState(
        row.Int64("epoch"),
        row.Int64("epoch_resets"),
        row.OptionalInt64("stored_highest_sequence"));
  }

  private static DateTimeOffset BucketStart(DateTimeOffset observedAt)
  {
    var utc = observedAt.UtcDateTime;
    return new DateTimeOffset(
        new DateTime(
            utc.Year,
            utc.Month,
            utc.Day,
            utc.Hour,
            0,
            0,
            DateTimeKind.Utc),
        TimeSpan.Zero);
  }

  private static string Utc(DateTimeOffset value) =>
      value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

  private static void AddNullable(
      SqliteCommand command,
      string name,
      object? value) =>
      command.Parameters.AddWithValue(name, value ?? DBNull.Value);

  private sealed record CursorState(
      long Epoch,
      long EpochResets,
      long? StoredHighestSequence);
}
