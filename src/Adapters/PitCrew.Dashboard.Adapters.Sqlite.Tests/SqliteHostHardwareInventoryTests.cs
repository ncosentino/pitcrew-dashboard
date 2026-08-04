using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteHostHardwareInventoryTests
{
  [Test]
  public async Task Hardware_Is_Deduplicated_Historic_And_Restart_Safe(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-hardware-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var store = new SqliteFleetStore(connectionFactory);
      var historyStore = new SqliteFleetHistoryStore(
          connectionFactory);
      var nodeId = Guid.NewGuid();
      var baseline = new DateTimeOffset(
          2026,
          8,
          3,
          12,
          0,
          0,
          TimeSpan.Zero);
      await InsertNodeAsync(
          connectionFactory,
          nodeId,
          cancellationToken);
      var hardware = CreateHardware(
          baseline,
          "a" + new string('0', 63),
          "Example Processor");
      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "6.0.0",
          baseline.AddMinutes(2),
          [
              CreateProfile("default", hardware),
              CreateProfile(
                  "build",
                  hardware with
                  {
                    AttemptedAt = baseline.AddMinutes(1),
                  }),
              CreateProfile(
                  "broken",
                  new HostHardwareInventory(
                      "unavailable",
                      null,
                      baseline.AddMinutes(2),
                      null,
                      null,
                      null,
                      null,
                      null,
                      null,
                      null,
                      null,
                      null,
                      null,
                      null,
                      null,
                      null)),
          ],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);

      var firstFleet = await store.GetFleetAsync(
          "tenant",
          baseline.AddMinutes(2),
          TimeSpan.FromMinutes(5),
          cancellationToken);
      await Assert.That(firstFleet.Nodes).HasSingleItem();
      await Assert.That(firstFleet.Nodes[0].Hardware).IsNotNull();
      await Assert.That(firstFleet.Nodes[0].Hardware!.AttemptedAt)
          .IsEqualTo(baseline.AddMinutes(1));
      await Assert.That(await CountRevisionsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(1);

      var changed = CreateHardware(
          baseline.AddMinutes(3),
          "b" + new string('0', 63),
          "Changed Processor");
      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "6.0.0",
          baseline.AddMinutes(4),
          [CreateProfile("default", changed)],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);
      await Assert.That(await CountRevisionsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(2);

      var restoredAt = baseline.AddMinutes(4).AddSeconds(30);
      var restored = hardware with
      {
        CollectedAt = restoredAt,
        AttemptedAt = restoredAt,
      };
      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "6.0.0",
          restoredAt,
          [CreateProfile("default", restored)],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);
      await Assert.That(await CountRevisionsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(3);

      var unavailable = new HostHardwareInventory(
          "unavailable",
          null,
          baseline.AddMinutes(5),
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null);
      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "6.0.0",
          baseline.AddMinutes(5),
          [CreateProfile("default", unavailable)],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);
      var restartedStore = new SqliteFleetStore(
          connectionFactory);
      var restartedFleet = await restartedStore.GetFleetAsync(
          "tenant",
          baseline.AddMinutes(5),
          TimeSpan.FromMinutes(5),
          cancellationToken);
      await Assert.That(restartedFleet.Nodes[0].Hardware).IsNotNull();
      await Assert.That(restartedFleet.Nodes[0].Hardware!.Status)
          .IsEqualTo("unavailable");
      await Assert.That(await CountRevisionsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(3);

      var reappearedAt = baseline.AddMinutes(5).AddSeconds(30);
      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "6.0.0",
          reappearedAt,
          [
              CreateProfile(
                  "default",
                  hardware with
                  {
                    CollectedAt = reappearedAt,
                    AttemptedAt = reappearedAt,
                  }),
          ],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);
      await Assert.That(await CountRevisionsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(4);

      var history = await historyStore.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          new HistoryWindow(
              baseline.AddMinutes(-1),
              baseline.AddMinutes(10),
              HistoryResolution.Raw,
              10,
              10,
              10,
              100,
              100,
              100),
          baseline.AddMinutes(10),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      await Assert.That(history!.HardwareRevisions).Count()
          .IsEqualTo(4);
      await Assert.That(history.HardwareRevisions[0].InventoryHash)
          .IsEqualTo(hardware.InventoryHash);
      await Assert.That(history.HardwareRevisions[1].InventoryHash)
          .IsEqualTo(hardware.InventoryHash);
      await Assert.That(history.HardwareRevisions[2].Hardware.ProcessorModel)
          .IsEqualTo("Changed Processor");
      await Assert.That(history.HardwareRevisions[3].InventoryHash)
          .IsEqualTo(hardware.InventoryHash);
      await Assert.That(history.HardwareRevisionsTruncated).IsFalse();

      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          nodeId,
          "5.0.0",
          baseline.AddMinutes(6),
          [
              CreateProfile("default", changed) with
              {
                ManagerContractVersion = 12,
                Host = null,
              },
          ],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);
      var downgradedFleet = await store.GetFleetAsync(
          "tenant",
          baseline.AddMinutes(6),
          TimeSpan.FromMinutes(5),
          cancellationToken);
      await Assert.That(downgradedFleet.Nodes[0].Hardware).IsNull();
      await Assert.That(await CountRevisionsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(4);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Stale_And_Future_Observations_Do_Not_Rewrite_Hardware(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-hardware-gates-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var store = new SqliteFleetStore(connectionFactory);
      var historyStore = new SqliteFleetHistoryStore(
          connectionFactory);
      var nodeId = Guid.NewGuid();
      var baseline = new DateTimeOffset(
          2026,
          8,
          3,
          12,
          0,
          0,
          TimeSpan.Zero);
      await InsertNodeAsync(
          connectionFactory,
          nodeId,
          cancellationToken);
      var policy = new HistoryAppendPolicy(
          CreateRetention(100),
          TimeSpan.FromMinutes(1));
      var original = CreateProfile(
          "default",
          CreateHardware(
              baseline,
              "a" + new string('0', 63),
              "Original Processor"));
      var changed = CreateProfile(
          "default",
          CreateHardware(
              baseline.AddMinutes(2),
              "b" + new string('0', 63),
              "Changed Processor"));
      foreach (var profile in new[] { original, changed })
      {
        await FleetStorageTestTransactions.ApplyAuthoritativeSyncAsync(
            store,
            historyStore,
            connectionFactory,
            nodeId,
            "6.0.0",
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
          CreateHardware(
              baseline.AddMinutes(1),
              "a" + new string('0', 63),
              "Original Processor"));
      await FleetStorageTestTransactions.ApplyAuthoritativeSyncAsync(
          store,
          historyStore,
          connectionFactory,
          nodeId,
          "6.0.0",
          baseline.AddMinutes(3),
          [stale],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          policy,
          cancellationToken);
      var future = CreateProfile(
          "default",
          CreateHardware(
              baseline.AddMinutes(10),
              "c" + new string('0', 63),
              "Future Processor"));
      await FleetStorageTestTransactions.ApplyAuthoritativeSyncAsync(
          store,
          historyStore,
          connectionFactory,
          nodeId,
          "6.0.0",
          baseline.AddMinutes(4),
          [future],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          policy,
          cancellationToken);

      var fleet = await store.GetFleetAsync(
          "tenant",
          baseline.AddMinutes(4),
          TimeSpan.FromMinutes(5),
          cancellationToken);
      await Assert.That(fleet.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Hardware).IsNotNull();
      await Assert.That(fleet.Nodes[0].Hardware!.ProcessorModel)
          .IsEqualTo("Changed Processor");
      await Assert.That(await CountRevisionsAsync(
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
  public async Task Advancing_Weaker_Profile_Preserves_Unchanged_Hardware(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-hardware-arbitration-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var store = new SqliteFleetStore(connectionFactory);
      var historyStore = new SqliteFleetHistoryStore(
          connectionFactory);
      var nodeId = Guid.NewGuid();
      var baseline = new DateTimeOffset(
          2026,
          8,
          3,
          12,
          0,
          0,
          TimeSpan.Zero);
      await InsertNodeAsync(
          connectionFactory,
          nodeId,
          cancellationToken);
      var policy = new HistoryAppendPolicy(
          CreateRetention(100),
          TimeSpan.FromMinutes(1));
      var stable = CreateProfile(
          "stable",
          CreateHardware(
              baseline,
              "a" + new string('0', 63),
              "Stable Processor"));
      await FleetStorageTestTransactions.ApplyAuthoritativeSyncAsync(
          store,
          historyStore,
          connectionFactory,
          nodeId,
          "6.0.0",
          baseline,
          [stable],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          policy,
          cancellationToken);

      var legacy = CreateProfile(
          "legacy",
          CreateHardware(
              baseline.AddMinutes(1),
              "b" + new string('0', 63),
              "Legacy Processor")) with
      {
        ManagerContractVersion = 12,
        Host = null,
      };
      await FleetStorageTestTransactions.ApplyAuthoritativeSyncAsync(
          store,
          historyStore,
          connectionFactory,
          nodeId,
          "6.0.0",
          baseline.AddMinutes(1),
          [stable, legacy],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          policy,
          cancellationToken);

      var unavailable = CreateProfile(
          "degraded",
          new HostHardwareInventory(
              "unavailable",
              null,
              baseline.AddMinutes(2),
              null,
              null,
              null,
              null,
              null,
              null,
              null,
              null,
              null,
              null,
              null,
              null,
              null));
      await FleetStorageTestTransactions.ApplyAuthoritativeSyncAsync(
          store,
          historyStore,
          connectionFactory,
          nodeId,
          "6.0.0",
          baseline.AddMinutes(2),
          [stable, legacy, unavailable],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          policy,
          cancellationToken);

      var fleet = await store.GetFleetAsync(
          "tenant",
          baseline.AddMinutes(2),
          TimeSpan.FromMinutes(5),
          cancellationToken);
      await Assert.That(fleet.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Hardware).IsNotNull();
      await Assert.That(fleet.Nodes[0].Hardware!.ProcessorModel)
          .IsEqualTo("Stable Processor");
      await Assert.That(await CountRevisionsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(1);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Hardware_Revisions_Are_Pruned_To_Diagnostic_Caps(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-hardware-retention-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var store = new SqliteFleetStore(connectionFactory);
      var historyStore = new SqliteFleetHistoryStore(
          connectionFactory);
      var nodeId = Guid.NewGuid();
      var baseline = new DateTimeOffset(
          2026,
          8,
          3,
          12,
          0,
          0,
          TimeSpan.Zero);
      await InsertNodeAsync(
          connectionFactory,
          nodeId,
          cancellationToken);

      for (var index = 1; index <= 3; index++)
      {
        var observedAt = baseline.AddMinutes(index);
        var hardware = CreateHardware(
            observedAt,
            index + new string('0', 63),
            $"Processor {index}");
        var profile = CreateProfile("default", hardware);
        await FleetStorageTestTransactions.ApplySyncAsync(
            store,
            connectionFactory,
            nodeId,
            "6.0.0",
            observedAt,
            [profile],
            new ConnectorCredentialUpdate(
                ConnectorCredentialUpdateKind.None,
                string.Empty),
            cancellationToken);
        await FleetStorageTestTransactions.AppendAsync(
            historyStore,
            connectionFactory,
            nodeId,
            [profile],
            observedAt,
            CreateRetention(2),
            cancellationToken);
      }

      await Assert.That(await CountRevisionsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(2);
      var history = await historyStore.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          new HistoryWindow(
              baseline,
              baseline.AddMinutes(4),
              HistoryResolution.Raw,
              10,
              10,
              10,
              100,
              100,
              100),
          baseline.AddMinutes(4),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      await Assert.That(history!.HardwareRevisions).Count()
          .IsEqualTo(2);
      await Assert.That(history.HardwareRevisions[0].Hardware.ProcessorModel)
          .IsEqualTo("Processor 3");
      await Assert.That(history.HardwareRevisions[1].Hardware.ProcessorModel)
          .IsEqualTo("Processor 2");
      await Assert.That(history.IncompletenessFloors.Single(
          floor => floor.Scope == "node").DroppedHardwareRevisions)
          .IsEqualTo(1);
      var profileHistory = await historyStore.GetProfileHistoryAsync(
          "tenant",
          nodeId,
          "default",
          new HistoryWindow(
              baseline,
              baseline.AddMinutes(4),
              HistoryResolution.Raw,
              10,
              10,
              10,
              100,
              100,
              100),
          baseline.AddMinutes(4),
          cancellationToken);
      await Assert.That(profileHistory).IsNotNull();
      await Assert.That(profileHistory!.IncompletenessFloors.All(
          floor => floor.DroppedHardwareRevisions == 0)).IsTrue();

      var secondNodeId = Guid.NewGuid();
      await InsertNodeAsync(
          connectionFactory,
          secondNodeId,
          cancellationToken);
      var secondObservedAt = baseline.AddMinutes(10);
      var secondHardware = CreateHardware(
          secondObservedAt,
          "4" + new string('0', 63),
          "Second Node Processor");
      var secondProfile = CreateProfile("default", secondHardware);
      await FleetStorageTestTransactions.ApplySyncAsync(
          store,
          connectionFactory,
          secondNodeId,
          "6.0.0",
          secondObservedAt,
          [secondProfile],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          historyStore,
          connectionFactory,
          secondNodeId,
          [secondProfile],
          secondObservedAt,
          CreateRetention(2, maximumHistoryNodes: 1),
          cancellationToken);

      await Assert.That(await CountRevisionsAsync(
          connectionFactory,
          nodeId,
          cancellationToken)).IsEqualTo(0);
      await Assert.That(await CountRevisionsAsync(
          connectionFactory,
          secondNodeId,
          cancellationToken)).IsEqualTo(1);
      var evictedHistory = await historyStore.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          new HistoryWindow(
              baseline,
              baseline.AddMinutes(20),
              HistoryResolution.Raw,
              10,
              10,
              10,
              100,
              100,
              100),
          baseline.AddMinutes(20),
          cancellationToken);
      await Assert.That(evictedHistory).IsNotNull();
      await Assert.That(evictedHistory!.HardwareRevisions).IsEmpty();
      await Assert.That(evictedHistory.IncompletenessFloors.Single(
          floor => floor.Scope == "node").DroppedHardwareRevisions)
          .IsEqualTo(3);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Global_Sweep_Applies_Hardware_Caps_Per_Node(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-hardware-global-retention-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var store = new SqliteFleetStore(connectionFactory);
      var historyStore = new SqliteFleetHistoryStore(
          connectionFactory);
      var baseline = new DateTimeOffset(
          2026,
          8,
          3,
          12,
          0,
          0,
          TimeSpan.Zero);
      var retention = CreateRetention(
          2,
          maximumDatabaseHardwareRevisions: 10,
          globalSweepInterval: TimeSpan.Zero);
      var nodeIds = new[]
      {
          Guid.NewGuid(),
          Guid.NewGuid(),
      };
      for (var nodeIndex = 0; nodeIndex < nodeIds.Length; nodeIndex++)
      {
        var nodeId = nodeIds[nodeIndex];
        await InsertNodeAsync(
            connectionFactory,
            nodeId,
            cancellationToken);
        for (var revisionIndex = 0; revisionIndex < 2; revisionIndex++)
        {
          var observedAt = baseline.AddMinutes(
              (nodeIndex * 10) + revisionIndex);
          var hashPrefix = (nodeIndex * 2) + revisionIndex + 1;
          var profile = CreateProfile(
              "default",
              CreateHardware(
                  observedAt,
                  hashPrefix + new string('0', 63),
                  $"Node {nodeIndex} Processor {revisionIndex}"));
          await FleetStorageTestTransactions.ApplySyncAsync(
              store,
              connectionFactory,
              nodeId,
              "6.0.0",
              observedAt,
              [profile],
              new ConnectorCredentialUpdate(
                  ConnectorCredentialUpdateKind.None,
                  string.Empty),
              cancellationToken);
          await FleetStorageTestTransactions.AppendAsync(
              historyStore,
              connectionFactory,
              nodeId,
              [profile],
              observedAt,
              retention,
              cancellationToken);
        }
      }

      foreach (var nodeId in nodeIds)
      {
        await Assert.That(await CountRevisionsAsync(
            connectionFactory,
            nodeId,
            cancellationToken)).IsEqualTo(2);
      }
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static HostHardwareInventory CreateHardware(
      DateTimeOffset observedAt,
      string hash,
      string processorModel) =>
      new(
          "current",
          observedAt,
          observedAt,
          hash,
          processorModel,
          "amd64",
          10,
          20,
          null,
          null,
          34359738368,
          "Docker Desktop",
          "6.12.34",
          "28.3.3",
          "overlayfs",
          "extfs");

  private static ManagerObservedState CreateProfile(
      string profileId,
      HostHardwareInventory hardware) =>
      new(
          1,
          13,
          profileId,
          $"manager-{profileId}",
          "running",
          hardware.AttemptedAt,
          "repo",
          1,
          new string('a', 64),
          "accepted",
          0,
          0,
          0,
          [],
          null,
          0,
          null,
          0,
          null,
          null,
          null,
          null,
          new ManagerWorkerUpdateState(
              "current",
              null,
              null,
              new string('b', 64),
              0,
              0,
              null),
          new ObservedHost(hardware));

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
        INSERT OR IGNORE INTO tenants (
            tenant_id,
            display_name,
            created_at)
        VALUES (
            'tenant',
            'Tenant',
            '2026-08-03T12:00:00.0000000+00:00');

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
            '2026-08-03T12:00:00.0000000+00:00');
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

  private static async Task<int> CountRevisionsAsync(
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
        FROM node_hardware_revisions
        WHERE node_id = $nodeId;
        """;
    command.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    return Convert.ToInt32(
        await command.ExecuteScalarAsync(cancellationToken),
        System.Globalization.CultureInfo.InvariantCulture);
  }

  private static HistoryRetentionPolicy CreateRetention(
      int maximumHardwareRevisions,
      int maximumHistoryNodes = 1000,
      int? maximumDatabaseHardwareRevisions = null,
      TimeSpan? globalSweepInterval = null) =>
      new(
          TimeSpan.FromDays(7),
          TimeSpan.FromDays(30),
          TimeSpan.FromDays(30),
          TimeSpan.FromDays(30),
          1000,
          1000,
          1000,
          10_000,
          10_000,
          10_000,
          maximumHardwareRevisions,
          1000,
          100_000,
          100_000,
          100_000,
          maximumDatabaseHardwareRevisions ??
              maximumHardwareRevisions,
          10_000,
          maximumHistoryNodes,
          globalSweepInterval ?? TimeSpan.FromMinutes(5),
          TimeSpan.FromDays(90));
}
