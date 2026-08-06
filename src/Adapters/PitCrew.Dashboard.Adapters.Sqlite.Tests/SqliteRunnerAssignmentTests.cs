using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteRunnerAssignmentTests
{
  [Test]
  public async Task Retains_Job_Lifecycle_And_Host_Pressure_With_Assignment(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-workload-history-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var historyStore = new SqliteFleetHistoryStore(connectionFactory);
      var nodeId = Guid.NewGuid();
      var baseline = new DateTimeOffset(
          2026,
          8,
          6,
          3,
          42,
          3,
          TimeSpan.Zero);
      await InsertNodeAsync(connectionFactory, nodeId, cancellationToken);
      var runnerHash = "a" + new string('0', 63);
      var job = new CurrentJobContext(
          "https://github.com/ncosentino/genesis",
          31068390178,
          "92513140749",
          "Android debug build",
          "push",
          baseline.AddMinutes(-2),
          baseline.AddMinutes(-1),
          baseline.AddSeconds(-30),
          baseline,
          null,
          null);
      var baseProfile = CreateProfile(
          "default",
          baseline,
          ("slot-1", runnerHash));
      var profile = baseProfile with
      {
        ManagerContractVersion = 16,
        Slots =
        [
            baseProfile.Slots[0] with
            {
              CurrentJob = job,
            },
        ],
        ResourceTelemetry = Telemetry(baseline),
      };
      await AppendAsync(
          historyStore,
          connectionFactory,
          nodeId,
          profile,
          baseline,
          cancellationToken);
      var completedAt = baseline.AddMinutes(20);
      await AppendAsync(
          historyStore,
          connectionFactory,
          nodeId,
          profile with
          {
            ObservedAt = completedAt,
            Slots =
            [
                profile.Slots[0] with
                {
                  CurrentJob = job with
                  {
                    FinishedAt = completedAt,
                    Result = "Cancelled",
                  },
                },
            ],
            ResourceTelemetry = Telemetry(completedAt),
          },
          completedAt,
          cancellationToken);

      var history = await historyStore.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          Window(baseline.AddMinutes(-1), completedAt.AddMinutes(1)),
          completedAt,
          cancellationToken);

      await Assert.That(history).IsNotNull();
      await Assert.That(history!.RunnerAssignments).HasSingleItem();
      await Assert.That(history.RunnerAssignments[0].Job).IsNotNull();
      await Assert.That(history.RunnerAssignments[0].Job!.Result)
          .IsEqualTo("Cancelled");
      await Assert.That(history.Profiles).HasSingleItem();
      await Assert.That(history.Profiles[0].Samples).Count().IsEqualTo(2);
      await Assert.That(
          history.Profiles[0].Samples[1].HostCpuUtilizationPercent)
          .IsEqualTo(97.5);
      await Assert.That(
          history.Profiles[0].Samples[1].HostIoPressureSomeAvg10)
          .IsEqualTo(42);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Rejected_Observation_Does_Not_Rewrite_Current_Correlation(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-runner-assignment-gates-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var fleetStore = new SqliteFleetStore(connectionFactory);
      var historyStore = new SqliteFleetHistoryStore(
          connectionFactory);
      var nodeId = Guid.NewGuid();
      var baseline = new DateTimeOffset(
          2026,
          8,
          4,
          12,
          0,
          0,
          TimeSpan.Zero);
      await InsertNodeAsync(
          connectionFactory,
          nodeId,
          cancellationToken);
      var policy = new HistoryAppendPolicy(
          Retention(maximumDiagnostics: 100),
          TimeSpan.FromMinutes(1));
      var firstHash = "a" + new string('0', 63);
      var currentHash = "b" + new string('0', 63);
      foreach (var profile in new[]
      {
          CreateProfile(
              "default",
              baseline,
              ("slot-1", firstHash)),
          CreateProfile(
              "default",
              baseline.AddMinutes(2),
              ("slot-2", currentHash)),
      })
      {
        await FleetStorageTestTransactions.ApplyAuthoritativeSyncAsync(
            fleetStore,
            historyStore,
            connectionFactory,
            nodeId,
            "7.0.0",
            profile.ObservedAt,
            [profile],
            new ConnectorCredentialUpdate(
                ConnectorCredentialUpdateKind.None,
                string.Empty),
            policy,
            cancellationToken);
      }

      var stale = CreateProfile(
          "default",
          baseline.AddMinutes(1),
          ("slot-stale", "c" + new string('0', 63)));
      await FleetStorageTestTransactions.ApplyAuthoritativeSyncAsync(
          fleetStore,
          historyStore,
          connectionFactory,
          nodeId,
          "7.0.0",
          baseline.AddMinutes(3),
          [stale],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          policy,
          cancellationToken);

      var rejectedOther = CreateProfile(
          "other",
          baseline.AddMinutes(10),
          ("slot-future", "d" + new string('0', 63)));
      await FleetStorageTestTransactions.ApplyAuthoritativeSyncAsync(
          fleetStore,
          historyStore,
          connectionFactory,
          nodeId,
          "7.0.0",
          baseline.AddMinutes(4),
          [rejectedOther],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          policy,
          cancellationToken);

      var fleet = await fleetStore.GetFleetAsync(
          "tenant",
          baseline.AddMinutes(4),
          TimeSpan.FromMinutes(5),
          cancellationToken);
      await Assert.That(fleet.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles).HasSingleItem();
      await Assert.That(
          fleet.Nodes[0].Profiles[0].Slots[0].RunnerNameHash)
          .IsEqualTo(currentHash);
      await Assert.That(await CountAssignmentsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(2);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Assignments_Are_Deduplicated_Filtered_And_Overlap_Queryable(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-runner-assignments-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var nodeId = Guid.NewGuid();
      var baseline = new DateTimeOffset(
          2026,
          8,
          4,
          12,
          0,
          0,
          TimeSpan.Zero);
      await InsertNodeAsync(
          connectionFactory,
          nodeId,
          cancellationToken);
      var firstHash = "a" + new string('0', 63);
      var secondHash = "b" + new string('0', 63);
      var otherHash = "c" + new string('0', 63);

      await AppendAsync(
          store,
          connectionFactory,
          nodeId,
          CreateProfile(
              "default",
              baseline,
              ("slot-1", firstHash)),
          baseline,
          cancellationToken);
      await AppendAsync(
          store,
          connectionFactory,
          nodeId,
          CreateProfile(
              "default",
              baseline.AddMinutes(1),
              ("slot-1", firstHash)),
          baseline.AddMinutes(1),
          cancellationToken);
      await AppendAsync(
          store,
          connectionFactory,
          nodeId,
          CreateProfile(
              "other",
              baseline.AddMinutes(1).AddSeconds(30),
              ("slot-other", otherHash)),
          baseline.AddMinutes(1).AddSeconds(30),
          cancellationToken);
      await AppendAsync(
          store,
          connectionFactory,
          nodeId,
          CreateProfile(
              "default",
              baseline.AddMinutes(2),
              ("slot-2", secondHash)),
          baseline.AddMinutes(2),
          cancellationToken);

      var restarted = new SqliteFleetHistoryStore(
          connectionFactory);
      var nodeHistory = await restarted.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          Window(
              baseline.AddSeconds(30),
              baseline.AddMinutes(1).AddSeconds(45)),
          baseline.AddMinutes(3),
          cancellationToken);
      await Assert.That(nodeHistory).IsNotNull();
      await Assert.That(nodeHistory!.RunnerAssignments).Count()
          .IsEqualTo(2);
      await Assert.That(nodeHistory.RunnerAssignments.Select(
          assignment => assignment.RunnerNameHash))
          .IsEquivalentTo([firstHash, otherHash]);
      var first = nodeHistory.RunnerAssignments.Single(
          assignment => assignment.RunnerNameHash == firstHash);
      await Assert.That(first.FirstObservedAt).IsEqualTo(baseline);
      await Assert.That(first.LastObservedAt)
          .IsEqualTo(baseline.AddMinutes(1));

      var profileHistory = await restarted.GetProfileHistoryAsync(
          "tenant",
          nodeId,
          "default",
          Window(baseline.AddMinutes(-1), baseline.AddMinutes(3)),
          baseline.AddMinutes(3),
          cancellationToken);
      await Assert.That(profileHistory).IsNotNull();
      await Assert.That(profileHistory!.RunnerAssignments).Count()
          .IsEqualTo(2);
      await Assert.That(profileHistory.RunnerAssignments.All(
          assignment => assignment.ProfileId == "default")).IsTrue();

      await AppendAsync(
          store,
          connectionFactory,
          nodeId,
          CreateProfile(
              "default",
              baseline.AddSeconds(30),
              ("slot-stale", "d" + new string('0', 63))),
          baseline.AddMinutes(4),
          cancellationToken);
      await Assert.That(await CountAssignmentsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(3);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Assignment_Retention_Is_Bounded_And_Explicit(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-runner-assignment-retention-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var nodeId = Guid.NewGuid();
      var baseline = new DateTimeOffset(
          2026,
          8,
          4,
          12,
          0,
          0,
          TimeSpan.Zero);
      await InsertNodeAsync(
          connectionFactory,
          nodeId,
          cancellationToken);
      var retention = Retention(maximumDiagnostics: 2);
      for (var index = 1; index <= 3; index++)
      {
        var observedAt = baseline.AddMinutes(index);
        var profile = CreateProfile(
            "default",
            observedAt,
            ($"slot-{index}", index + new string('0', 63)));
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [profile],
            observedAt,
            retention,
            cancellationToken);
      }

      await Assert.That(await CountAssignmentsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(2);
      var history = await store.GetProfileHistoryAsync(
          "tenant",
          nodeId,
          "default",
          Window(baseline, baseline.AddMinutes(4)),
          baseline.AddMinutes(4),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      await Assert.That(history!.RunnerAssignments).Count()
          .IsEqualTo(2);
      await Assert.That(history.Profiles).HasSingleItem();
      await Assert.That(
          history.Profiles[0].Retention.DroppedRunnerAssignments)
          .IsEqualTo(1);
      await Assert.That(
          history.Profiles[0].Retention.EarliestRetainedRunnerAssignment)
          .IsEqualTo(baseline.AddMinutes(2));
      await Assert.That(history.IncompletenessFloors.All(
          floor => floor.DroppedRunnerAssignments == 0)).IsTrue();

      var truncated = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          Window(
              baseline,
              baseline.AddMinutes(4),
              diagnosticLimit: 1),
          baseline.AddMinutes(4),
          cancellationToken);
      await Assert.That(truncated).IsNotNull();
      await Assert.That(truncated!.RunnerAssignments).HasSingleItem();
      await Assert.That(truncated.RunnerAssignmentsTruncated).IsTrue();

      var otherObservedAt = baseline.AddMinutes(5);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  "other",
                  otherObservedAt,
                  ("slot-other", "4" + new string('0', 63))),
          ],
          otherObservedAt,
          Retention(
              maximumDiagnostics: 2,
              maximumProfilesPerNode: 1),
          cancellationToken);
      var expired = await store.GetProfileHistoryAsync(
          "tenant",
          nodeId,
          "default",
          Window(baseline, baseline.AddMinutes(6)),
          baseline.AddMinutes(6),
          cancellationToken);
      await Assert.That(expired).IsNotNull();
      await Assert.That(expired!.RunnerAssignments).IsEmpty();
      await Assert.That(expired.Profiles).HasSingleItem();
      await Assert.That(expired.Profiles[0].Journal.Status)
          .IsEqualTo("expired");
      await Assert.That(
          expired.Profiles[0].Retention.DroppedRunnerAssignments)
          .IsEqualTo(3);

      var compactedAt = baseline.AddMinutes(6);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  "other",
                  compactedAt,
                  ("slot-other", "4" + new string('0', 63))),
          ],
          compactedAt,
          Retention(
              maximumDiagnostics: 2,
              maximumProfilesPerNode: 0),
          cancellationToken);
      var compacted = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          Window(baseline, baseline.AddMinutes(7)),
          baseline.AddMinutes(7),
          cancellationToken);
      await Assert.That(compacted).IsNotNull();
      await Assert.That(compacted!.IncompletenessFloors.Single(
          floor => floor.Scope == "node").DroppedRunnerAssignments)
          .IsEqualTo(4);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static async Task<SqliteConnectionFactory> CreateDatabaseAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var connectionFactory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
        cancellationToken);
    return connectionFactory;
  }

  private static async Task AppendAsync(
      SqliteFleetHistoryStore store,
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      ManagerObservedState profile,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken) =>
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [profile],
          receivedAt,
          Retention(maximumDiagnostics: 100),
          cancellationToken);

  private static HistoryWindow Window(
      DateTimeOffset from,
      DateTimeOffset to,
      int diagnosticLimit = 100) =>
      new(
          from,
          to,
          HistoryResolution.Raw,
          100,
          100,
          diagnosticLimit,
          1000,
          1000,
          diagnosticLimit);

  private static ManagerObservedState CreateProfile(
      string profileId,
      DateTimeOffset observedAt,
      params (string SlotKey, string RunnerNameHash)[] assignments)
  {
    var slots = assignments
        .Select(assignment => new ObservedSlotState(
            assignment.SlotKey,
            "https://github.com/example/project",
            true,
            true,
            "online",
            0,
            0,
            observedAt,
            null,
            "busy",
            "repo:example/project",
            "connected",
            null,
            null,
            assignment.RunnerNameHash))
        .ToArray();
    return new ManagerObservedState(
        1,
        14,
        profileId,
        $"manager-{profileId}",
        "running",
        observedAt,
        "repo",
        1,
        new string('a', 64),
        "accepted",
        slots.Length,
        slots.Length,
        0,
        slots,
        null,
        slots.Length,
        null,
        slots.Length);
  }

  private static ManagerResourceTelemetry Telemetry(
      DateTimeOffset sampledAt) =>
      new(
          sampledAt,
          "unavailable",
          null,
          null,
          new HostPressureTelemetry(
              "available",
              "docker-host",
              97.5,
              18,
              12,
              8,
              34359738368,
              2147483648,
              1073741824,
              35,
              5,
              25,
              3,
              42,
              18));

  private static HistoryRetentionPolicy Retention(
      int maximumDiagnostics,
      int maximumProfilesPerNode = 1000) =>
      new(
          TimeSpan.FromDays(7),
          TimeSpan.FromDays(30),
          TimeSpan.FromDays(30),
          TimeSpan.FromDays(30),
          1000,
          1000,
          maximumDiagnostics,
          10_000,
          10_000,
          10_000,
          maximumDiagnostics,
          maximumProfilesPerNode,
          100_000,
          100_000,
          100_000,
          100_000,
          10_000,
          1000,
          TimeSpan.FromMinutes(5),
          TimeSpan.FromDays(90));

  private static async Task InsertNodeAsync(
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO tenants (tenant_id, display_name, created_at)
        VALUES (
            'tenant',
            'Tenant',
            '2026-08-04T12:00:00.0000000+00:00');

        INSERT INTO nodes (
            node_id,
            tenant_id,
            connector_instance_id,
            display_name,
            credential_hash,
            connector_version,
            enrolled_at)
        VALUES (
            $nodeId,
            'tenant',
            $connectorInstanceId,
            'Node',
            $credentialHash,
            '',
            '2026-08-04T12:00:00.0000000+00:00');
        """;
    command.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$connectorInstanceId",
        $"connector-{nodeId:N}");
    command.Parameters.AddWithValue(
        "$credentialHash",
        $"credential-{nodeId:N}");
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<int> CountAssignmentsAsync(
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT COUNT(*)
        FROM profile_runner_assignments
        WHERE node_id = $nodeId;
        """;
    command.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    return Convert.ToInt32(
        await command.ExecuteScalarAsync(cancellationToken),
        System.Globalization.CultureInfo.InvariantCulture);
  }
}
