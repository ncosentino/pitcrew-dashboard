using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteConnectorHealthStoreTests
{
  [Test]
  public async Task Replay_Rolls_Back_With_The_Fleet_Transaction(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-connector-health-rollback-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = CreateConnectionFactory(databasePath);
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var nodeId = Guid.NewGuid();
      await SeedNodeAsync(
          connectionFactory,
          nodeId,
          cancellationToken);
      var store = new SqliteConnectorHealthStore(
          connectionFactory);
      var now = new DateTimeOffset(
          2026,
          8,
          7,
          11,
          30,
          0,
          TimeSpan.Zero);

      await using (var transaction =
          await SqliteFleetTransaction.BeginAsync(
              connectionFactory,
              cancellationToken))
      {
        await store.ApplyAsync(
            transaction,
            nodeId,
            CreateReplay(
                now,
                [
                    CreateEvent(
                        Guid.NewGuid(),
                        now,
                        1),
                ]),
            now,
            new ConnectorHealthRetentionPolicy(
                TimeSpan.FromDays(30),
                10),
            cancellationToken);
      }
      var projection = await store.GetAsync(
          "tenant",
          nodeId,
          10,
          cancellationToken);

      await Assert.That(projection).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Replay_Is_Idempotent_Tenant_Scoped_And_Bounded(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-connector-health-store-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = CreateConnectionFactory(databasePath);
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var nodeId = Guid.NewGuid();
      await SeedNodeAsync(
          connectionFactory,
          nodeId,
          cancellationToken);
      var store = new SqliteConnectorHealthStore(
          connectionFactory);
      var now = new DateTimeOffset(
          2026,
          8,
          7,
          12,
          0,
          0,
          TimeSpan.Zero);
      var firstEventId = Guid.NewGuid();
      var secondEventId = Guid.NewGuid();
      var replay = CreateReplay(
          now,
          [
              CreateEvent(
                  firstEventId,
                  now.AddMinutes(-2),
                  1),
              CreateEvent(
                  secondEventId,
                  now.AddMinutes(-1),
                  2),
          ]);
      var retention = new ConnectorHealthRetentionPolicy(
          TimeSpan.FromDays(30),
          10);

      await ApplyAsync(
          store,
          connectionFactory,
          nodeId,
          replay,
          now,
          retention,
          cancellationToken);
      await ApplyAsync(
          store,
          connectionFactory,
          nodeId,
          replay,
          now.AddSeconds(1),
          retention,
          cancellationToken);

      var projection = await store.GetAsync(
          "tenant",
          nodeId,
          10,
          cancellationToken);
      var unauthorized = await store.GetAsync(
          "other-tenant",
          nodeId,
          10,
          cancellationToken);

      await Assert.That(projection).IsNotNull();
      await Assert.That(projection!.Snapshot.State)
          .IsEqualTo("degraded");
      await Assert.That(projection.ReceivedAt)
          .IsEqualTo(now.AddSeconds(1));
      await Assert.That(projection.Events).Count().IsEqualTo(2);
      await Assert.That(projection.Events[0].EventId)
          .IsEqualTo(secondEventId);
      await Assert.That(projection.HistoryTruncated).IsFalse();
      await Assert.That(unauthorized).IsNull();

      var staleSnapshot = replay.Snapshot with
      {
        State = "healthy",
        ActiveOutageId = null,
        ActiveOutageStartedAt = null,
        ConsecutiveFailures = 0,
        NextRetryAt = null,
      };
      await ApplyAsync(
          store,
          connectionFactory,
          nodeId,
          new ConnectorHealthReplay(
              staleSnapshot,
              []),
          now,
          retention,
          cancellationToken);
      var afterStale = await store.GetAsync(
          "tenant",
          nodeId,
          10,
          cancellationToken);

      await Assert.That(afterStale!.Snapshot.State)
          .IsEqualTo("degraded");
      await Assert.That(afterStale.ReceivedAt)
          .IsEqualTo(now.AddSeconds(1));

      var thirdEventId = Guid.NewGuid();
      await ApplyAsync(
          store,
          connectionFactory,
          nodeId,
          CreateReplay(
              now.AddMinutes(1),
              [
                  CreateEvent(
                      thirdEventId,
                      now.AddMinutes(1),
                      3),
              ]),
          now.AddMinutes(1),
          retention with
          {
            MaximumEventsPerNode = 2,
          },
          cancellationToken);
      var bounded = await store.GetAsync(
          "tenant",
          nodeId,
          1,
          cancellationToken);

      await Assert.That(bounded).IsNotNull();
      await Assert.That(bounded!.Events).HasSingleItem();
      await Assert.That(bounded.Events[0].EventId)
          .IsEqualTo(thirdEventId);
      await Assert.That(bounded.HistoryTruncated).IsTrue();

      var future = now.AddDays(40);
      var offsetFuture = future.ToOffset(
          TimeSpan.FromHours(2));
      var fourthEventId = Guid.NewGuid();
      await ApplyAsync(
          store,
          connectionFactory,
          nodeId,
          CreateReplay(
              offsetFuture,
              [
                  CreateEvent(
                      fourthEventId,
                      offsetFuture,
                      1),
              ]),
          future,
          retention,
          cancellationToken);
      var ageBounded = await store.GetAsync(
          "tenant",
          nodeId,
          10,
          cancellationToken);

      await Assert.That(ageBounded).IsNotNull();
      await Assert.That(ageBounded!.Events).HasSingleItem();
      await Assert.That(ageBounded.Events[0].EventId)
          .IsEqualTo(fourthEventId);
      await Assert.That(ageBounded.Events[0].OccurredAt.Offset)
          .IsEqualTo(TimeSpan.Zero);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Migration_Seventeen_Preserves_Existing_Nodes(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-connector-health-migration-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = CreateConnectionFactory(databasePath);
      await SqliteMigrationTestDatabase.ApplyThroughAsync(
          connectionFactory,
          16,
          cancellationToken);
      var nodeId = Guid.NewGuid();
      await SeedNodeAsync(
          connectionFactory,
          nodeId,
          cancellationToken);

      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);

      await using var connection = await connectionFactory.OpenAsync(
          cancellationToken);
      await using var command = connection.CreateCommand();
      command.CommandText =
          """
          SELECT
              (SELECT COUNT(*)
               FROM nodes
               WHERE node_id = $nodeId),
              (SELECT COUNT(*)
               FROM sqlite_master
               WHERE type = 'table'
                 AND name IN (
                     'connector_health_current',
                     'connector_health_events')),
              (SELECT MAX(version)
               FROM schema_migrations);
          """;
      command.Parameters.AddWithValue(
          "$nodeId",
          nodeId.ToString("D"));
      await using var reader = await command.ExecuteReaderAsync(
          cancellationToken);
      await reader.ReadAsync(cancellationToken);

      await Assert.That(reader.GetInt32(0)).IsEqualTo(1);
      await Assert.That(reader.GetInt32(1)).IsEqualTo(2);
      await Assert.That(reader.GetInt32(2)).IsEqualTo(28);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static async Task ApplyAsync(
      SqliteConnectorHealthStore store,
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      ConnectorHealthReplay replay,
      DateTimeOffset receivedAt,
      ConnectorHealthRetentionPolicy retention,
      CancellationToken cancellationToken)
  {
    await using var transaction = await SqliteFleetTransaction.BeginAsync(
        connectionFactory,
        cancellationToken);
    await store.ApplyAsync(
        transaction,
        nodeId,
        replay,
        receivedAt,
        retention,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  private static ConnectorHealthReplay CreateReplay(
      DateTimeOffset updatedAt,
      IReadOnlyList<ConnectorHealthReplayEvent> events)
  {
    var outageId = new Guid(
        "11111111-1111-1111-1111-111111111111");
    return new ConnectorHealthReplay(
        new ConnectorHealthReplaySnapshot(
            "degraded",
            updatedAt.AddHours(-1),
            updatedAt,
            updatedAt,
            updatedAt.AddMinutes(-5),
            outageId,
            updatedAt.AddMinutes(-4),
            updatedAt,
            "synchronization-network",
            null,
            "Connector synchronization could not reach Dashboard.",
            3,
            updatedAt.AddMinutes(5),
            null,
            null,
            null,
            null),
        events);
  }

  private static ConnectorHealthReplayEvent CreateEvent(
      Guid eventId,
      DateTimeOffset occurredAt,
      int consecutiveFailures) =>
      new(
          eventId,
          "synchronization-failed",
          occurredAt,
          "degraded",
          new Guid(
              "11111111-1111-1111-1111-111111111111"),
          occurredAt.AddMinutes(-1),
          "synchronization-network",
          null,
          consecutiveFailures,
          300,
          "Connector synchronization could not reach Dashboard.");

  private static SqliteConnectionFactory CreateConnectionFactory(
      string databasePath) =>
      new(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));

  private static async Task SeedNodeAsync(
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO tenants (
            tenant_id,
            display_name,
            created_at)
        VALUES (
            'tenant',
            'Tenant',
            $createdAt);

        INSERT INTO nodes (
            node_id,
            tenant_id,
            connector_instance_id,
            display_name,
            credential_hash,
            enrolled_at)
        VALUES (
            $nodeId,
            'tenant',
            'connector-instance',
            'Connector',
            $credentialHash,
            $createdAt);
        """;
    command.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    command.Parameters.AddWithValue(
        "$credentialHash",
        $"credential-{nodeId:N}");
    command.Parameters.AddWithValue(
        "$createdAt",
        new DateTimeOffset(
            2026,
            8,
            7,
            11,
            0,
            0,
            TimeSpan.Zero).ToString(
                "O",
                CultureInfo.InvariantCulture));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }
}
