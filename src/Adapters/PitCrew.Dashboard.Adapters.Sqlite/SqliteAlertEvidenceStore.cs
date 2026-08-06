using System.Globalization;
using System.Text.Json;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteAlertEvidenceStore(
    SqliteConnectionFactory _connectionFactory) : IAlertEvidenceStore
{
  public async Task<AlertEvidenceSnapshot> LoadAsync(
      DateTimeOffset resourceWindowStart,
      int maximumSamplesPerProfile,
      CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using var transaction = (SqliteTransaction)
        await connection.BeginTransactionAsync(cancellationToken);
    var nodes = await LoadNodesAsync(
        connection,
        transaction,
        cancellationToken);
    var profiles = await LoadProfilesAsync(
        connection,
        transaction,
        cancellationToken);
    await LoadResourceSamplesAsync(
        connection,
        transaction,
        profiles,
        resourceWindowStart,
        maximumSamplesPerProfile,
        cancellationToken);
    await LoadHostPressureSamplesAsync(
        connection,
        transaction,
        nodes,
        resourceWindowStart,
        maximumSamplesPerProfile,
        cancellationToken);
    await LoadCapacityCommandsAsync(
        connection,
        transaction,
        profiles,
        cancellationToken);
    await LoadRecoveryCommandsAsync(
        connection,
        transaction,
        profiles,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    foreach (var profile in profiles.Values)
    {
      if (nodes.TryGetValue(profile.NodeId, out var node))
      {
        node.Profiles.Add(profile);
      }
    }

    return new AlertEvidenceSnapshot(
        nodes.Values
            .OrderBy(node => node.TenantId, StringComparer.Ordinal)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal)
            .Select(node => node.Build())
            .ToArray());
  }

  private static async Task<Dictionary<string, NodeBuilder>> LoadNodesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            node_id,
            tenant_id,
            COALESCE(display_name_override, display_name) AS display_name,
            enrolled_at,
            last_seen_at,
            revoked_at IS NOT NULL AS is_revoked
        FROM nodes
        ORDER BY tenant_id, node_id;
        """;
    var result = new Dictionary<string, NodeBuilder>(
        StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    var row = new SqliteRowReader(reader);
    while (await reader.ReadAsync(cancellationToken))
    {
      var nodeId = row.String("node_id");
      result.Add(
          nodeId,
          new NodeBuilder(
              nodeId,
              row.String("tenant_id"),
              row.String("display_name"),
              row.Time("enrolled_at"),
              row.OptionalTime("last_seen_at"),
              row.Int64("is_revoked") == 1));
    }
    return result;
  }

  private static async Task<Dictionary<ProfileKey, ProfileBuilder>>
      LoadProfilesAsync(
          SqliteConnection connection,
          SqliteTransaction transaction,
          CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            p.node_id,
            p.profile_id,
            p.payload_json,
            COALESCE(c.journal_status, 'unreported') AS journal_status,
            COALESCE(c.manager_dropped_events, 0)
                AS manager_dropped_events,
            COALESCE(c.missed_events, 0) AS missed_events,
            COALESCE(c.epoch_resets, 0) AS epoch_resets,
            COALESCE(c.rejected_future_events, 0)
                AS rejected_future_events,
            c.manager_highest_sequence,
            c.stored_highest_sequence,
            c.history_expired_at
        FROM profiles AS p
        LEFT JOIN profile_history_cursors AS c
          ON c.node_id = p.node_id
         AND c.profile_id = p.profile_id
        ORDER BY p.node_id, p.profile_id;
        """;
    var result = new Dictionary<ProfileKey, ProfileBuilder>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    var row = new SqliteRowReader(reader);
    while (await reader.ReadAsync(cancellationToken))
    {
      var nodeId = row.String("node_id");
      var profileId = row.String("profile_id");
      var observation = JsonSerializer.Deserialize(
          row.String("payload_json"),
          PitCrewProtocolJsonContext.Default.ManagerObservedState) ??
          throw new InvalidOperationException(
              $"Profile '{profileId}' on node '{nodeId}' has no readable observation.");
      var managerHighest = row.OptionalInt64("manager_highest_sequence");
      var storedHighest = row.OptionalInt64("stored_highest_sequence");
      var undelivered = managerHighest is null || storedHighest is null
          ? 0
          : Math.Max(0, managerHighest.Value - storedHighest.Value);
      var key = new ProfileKey(nodeId, profileId);
      result.Add(
          key,
          new ProfileBuilder(
              nodeId,
              observation,
              new AlertJournalEvidence(
                  row.String("journal_status"),
                  row.Int32("manager_dropped_events"),
                  row.Int64("missed_events"),
                  undelivered,
                  row.Int64("epoch_resets"),
                  row.Int64("rejected_future_events"),
                  row.OptionalTime("history_expired_at"))));
    }
    return result;
  }

  private static async Task LoadResourceSamplesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      IReadOnlyDictionary<ProfileKey, ProfileBuilder> profiles,
      DateTimeOffset resourceWindowStart,
      int maximumSamplesPerProfile,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        WITH ranked AS (
            SELECT
                s.node_id,
                s.profile_id,
                s.observed_at,
                s.telemetry_status,
                s.manager_cpu_cores,
                s.worker_cpu_cores,
                s.host_logical_processors,
                s.manager_memory_bytes,
                s.worker_memory_bytes,
                s.local_running_workers,
                s.host_memory_bytes,
                s.network_rx_bytes,
                s.network_tx_bytes,
                s.block_read_bytes,
                s.block_write_bytes,
                ROW_NUMBER() OVER (
                    PARTITION BY s.node_id, s.profile_id
                    ORDER BY s.observed_at DESC) AS rank_index
            FROM profiles AS p
            CROSS JOIN profile_telemetry_samples AS s
            WHERE s.node_id = p.node_id
              AND s.profile_id = p.profile_id
              AND s.observed_at >= $from)
        SELECT
            node_id,
            profile_id,
            observed_at,
            telemetry_status,
            manager_cpu_cores,
            worker_cpu_cores,
            host_logical_processors,
            manager_memory_bytes,
            worker_memory_bytes,
            local_running_workers,
            host_memory_bytes,
            network_rx_bytes,
            network_tx_bytes,
            block_read_bytes,
            block_write_bytes
        FROM ranked
        WHERE rank_index <= $maximum
        ORDER BY node_id, profile_id, observed_at;
        """;
    command.Parameters.AddWithValue(
        "$from",
        Utc(resourceWindowStart));
    command.Parameters.AddWithValue("$maximum", maximumSamplesPerProfile);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    var row = new SqliteRowReader(reader);
    while (await reader.ReadAsync(cancellationToken))
    {
      var key = new ProfileKey(
          row.String("node_id"),
          row.String("profile_id"));
      if (!profiles.TryGetValue(key, out var profile))
      {
        continue;
      }
      profile.ResourceSamples.Add(new AlertResourceSample(
          row.Time("observed_at"),
          row.String("telemetry_status"),
          SumUsage(
              row.OptionalDouble("manager_cpu_cores"),
              row.OptionalDouble("worker_cpu_cores"),
              row.Int32("local_running_workers")),
          row.OptionalInt32("host_logical_processors"),
          SumUsage(
              row.OptionalInt64("manager_memory_bytes"),
              row.OptionalInt64("worker_memory_bytes"),
              row.Int32("local_running_workers")),
          row.OptionalInt64("host_memory_bytes"),
          Sum(
              row.OptionalInt64("network_rx_bytes"),
              row.OptionalInt64("network_tx_bytes")),
          Sum(
              row.OptionalInt64("block_read_bytes"),
              row.OptionalInt64("block_write_bytes"))));
    }
  }

  private static async Task LoadCapacityCommandsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      IReadOnlyDictionary<ProfileKey, ProfileBuilder> profiles,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            command_id,
            node_id,
            profile_id,
            status,
            completed_at,
            result_message
        FROM (
            SELECT
                c.command_id,
                c.node_id,
                c.profile_id,
                c.status,
                c.completed_at,
                c.result_message,
                ROW_NUMBER() OVER (
                    PARTITION BY c.node_id, c.profile_id
                    ORDER BY c.requested_at DESC, c.command_id DESC)
                    AS rank_index
            FROM profiles AS p
            CROSS JOIN capacity_commands AS c
            WHERE c.node_id = p.node_id
              AND c.profile_id = p.profile_id)
        WHERE rank_index = 1;
        """;
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    var row = new SqliteRowReader(reader);
    while (await reader.ReadAsync(cancellationToken))
    {
      var key = new ProfileKey(
          row.String("node_id"),
          row.String("profile_id"));
      if (profiles.TryGetValue(key, out var profile))
      {
        profile.LatestCapacityCommand = new AlertCommandEvidence(
            Guid.Parse(
                row.String("command_id"),
                CultureInfo.InvariantCulture),
            "capacity",
            row.String("status"),
            row.OptionalTime("completed_at"),
            null,
            row.OptionalString("result_message"));
      }

    }
  }

  private static async Task LoadHostPressureSamplesAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      IReadOnlyDictionary<string, NodeBuilder> nodes,
      DateTimeOffset resourceWindowStart,
      int maximumSamplesPerNode,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        WITH heartbeat_samples AS (
            SELECT
                node_id,
                recorded_at,
                observed_at,
                host_pressure_status,
                host_logical_processors,
                host_cpu_utilization_percent,
                host_load1,
                host_pressure_memory_total_bytes,
                host_memory_available_bytes,
                host_cpu_pressure_some_avg10,
                host_memory_pressure_some_avg10,
                host_io_pressure_some_avg10,
                ROW_NUMBER() OVER (
                    PARTITION BY node_id, recorded_at
                    ORDER BY
                        CASE host_pressure_status
                            WHEN 'available' THEN 0
                            WHEN 'partial' THEN 1
                            ELSE 2
                        END,
                        observed_at DESC,
                        profile_id) AS heartbeat_index
            FROM profile_telemetry_samples
            WHERE recorded_at >= $from
              AND host_pressure_status IS NOT NULL),
        ranked AS (
            SELECT
                *,
                ROW_NUMBER() OVER (
                    PARTITION BY node_id
                    ORDER BY recorded_at DESC) AS rank_index
            FROM heartbeat_samples
            WHERE heartbeat_index = 1)
        SELECT
            node_id,
            recorded_at,
            host_pressure_status,
            host_logical_processors,
            host_cpu_utilization_percent,
            host_load1,
            host_pressure_memory_total_bytes,
            host_memory_available_bytes,
            host_cpu_pressure_some_avg10,
            host_memory_pressure_some_avg10,
            host_io_pressure_some_avg10
        FROM ranked
        WHERE rank_index <= $maximum
        ORDER BY node_id, recorded_at;
        """;
    command.Parameters.AddWithValue("$from", Utc(resourceWindowStart));
    command.Parameters.AddWithValue("$maximum", maximumSamplesPerNode);
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    var row = new SqliteRowReader(reader);
    while (await reader.ReadAsync(cancellationToken))
    {
      var nodeId = row.String("node_id");
      if (!nodes.TryGetValue(nodeId, out var node))
      {
        continue;
      }
      node.HostPressureSamples.Add(new AlertHostPressureSample(
          row.Time("recorded_at"),
          row.String("host_pressure_status"),
          row.OptionalInt32("host_logical_processors"),
          row.OptionalDouble("host_cpu_utilization_percent"),
          row.OptionalDouble("host_load1"),
          row.OptionalInt64("host_pressure_memory_total_bytes"),
          row.OptionalInt64("host_memory_available_bytes"),
          row.OptionalDouble("host_cpu_pressure_some_avg10"),
          row.OptionalDouble("host_memory_pressure_some_avg10"),
          row.OptionalDouble("host_io_pressure_some_avg10")));
    }
  }

  private static async Task LoadRecoveryCommandsAsync(
      SqliteConnection connection,
      SqliteTransaction transaction,
      IReadOnlyDictionary<ProfileKey, ProfileBuilder> profiles,
      CancellationToken cancellationToken)
  {
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText =
        """
        SELECT
            command_id,
            node_id,
            profile_id,
            status,
            completed_at,
            failure_category,
            result_message
        FROM (
            SELECT
                c.command_id,
                c.node_id,
                c.profile_id,
                c.status,
                c.completed_at,
                c.failure_category,
                c.result_message,
                ROW_NUMBER() OVER (
                    PARTITION BY c.node_id, c.profile_id
                    ORDER BY c.requested_at DESC, c.command_id DESC)
                    AS rank_index
            FROM profiles AS p
            CROSS JOIN recovery_commands AS c
            WHERE c.node_id = p.node_id
              AND c.profile_id = p.profile_id)
        WHERE rank_index = 1;
        """;
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    var row = new SqliteRowReader(reader);
    while (await reader.ReadAsync(cancellationToken))
    {
      var key = new ProfileKey(
          row.String("node_id"),
          row.String("profile_id"));
      if (profiles.TryGetValue(key, out var profile))
      {
        profile.LatestRecoveryCommand = new AlertCommandEvidence(
            Guid.Parse(
                row.String("command_id"),
                CultureInfo.InvariantCulture),
            "recovery",
            row.String("status"),
            row.OptionalTime("completed_at"),
            row.OptionalString("failure_category"),
            row.OptionalString("result_message"));
      }
    }
  }

  private static double? Sum(double? left, double? right) =>
      left is null || right is null
          ? null
          : left.Value + right.Value;

  private static long? Sum(long? left, long? right) =>
      left is null || right is null
          ? null
          : checked(left.Value + right.Value);

  private static double? SumUsage(
      double? manager,
      double? workers,
      int localRunningWorkers) =>
      manager is null ||
      workers is null && localRunningWorkers > 0
          ? null
          : manager.Value + (workers ?? 0);

  private static long? SumUsage(
      long? manager,
      long? workers,
      int localRunningWorkers) =>
      manager is null ||
      workers is null && localRunningWorkers > 0
          ? null
          : checked(manager.Value + (workers ?? 0));

  private static string Utc(DateTimeOffset value) =>
      value.ToUniversalTime().ToString(
          "O",
          CultureInfo.InvariantCulture);

  private readonly record struct ProfileKey(
      string NodeId,
      string ProfileId);

  private sealed class NodeBuilder(
      string nodeId,
      string tenantId,
      string displayName,
      DateTimeOffset enrolledAt,
      DateTimeOffset? lastSeenAt,
      bool isRevoked)
  {
    public string NodeId { get; } = nodeId;

    public string TenantId { get; } = tenantId;

    public List<ProfileBuilder> Profiles { get; } = [];

    public List<AlertHostPressureSample> HostPressureSamples { get; } = [];

    public AlertNodeEvidence Build() =>
        new(
            TenantId,
            Guid.Parse(NodeId, CultureInfo.InvariantCulture),
            displayName,
            enrolledAt,
            lastSeenAt,
            isRevoked,
            Profiles
                .OrderBy(
                    profile => profile.Observation.ProfileId,
                    StringComparer.Ordinal)
                .Select(profile => profile.Build())
                .ToArray())
        {
          RecentHostPressureSamples = HostPressureSamples,
        };
  }

  private sealed class ProfileBuilder(
      string nodeId,
      ManagerObservedState observation,
      AlertJournalEvidence journal)
  {
    public string NodeId { get; } = nodeId;

    public ManagerObservedState Observation { get; } = observation;

    public AlertJournalEvidence Journal { get; } = journal;

    public List<AlertResourceSample> ResourceSamples { get; } = [];

    public AlertCommandEvidence? LatestCapacityCommand { get; set; }

    public AlertCommandEvidence? LatestRecoveryCommand { get; set; }

    public AlertProfileEvidence Build() =>
        new(
            Observation,
            Journal,
            ResourceSamples,
            LatestCapacityCommand,
            LatestRecoveryCommand);
  }
}
