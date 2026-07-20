using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteFleetStoreTests
{
  [Test]
  public async Task Resource_Telemetry_Round_Trips_And_Legacy_Payload_Remains_Readable(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-fleet-{Guid.NewGuid():N}.db");
    try
    {
      var observedAt = new DateTimeOffset(
          2026,
          7,
          19,
          18,
          30,
          0,
          TimeSpan.Zero);
      var (connectionFactory, store, nodeId) =
          await CreateEnrolledStoreAsync(
              databasePath,
              observedAt,
              cancellationToken);
      var expectedSlotResources = new ResourceUsage(
          1.75,
          805_306_368,
          37);
      var expectedTelemetry = new ManagerResourceTelemetry(
          observedAt,
          "available",
          new HostResourceCapacity(
              16,
              68_719_476_736),
          new ResourceUsage(
              0.5,
              201_326_592,
              11));
      var profile = new ManagerObservedState(
          1,
          7,
          "default",
          "manager-instance",
          "running",
          observedAt,
          "repo",
          1,
          new string('a', 64),
          "accepted",
          1,
          1,
          0,
          [
              new ObservedSlotState(
                  "repo-example-000001",
                  "https://github.com/example/project",
                  true,
                  true,
                  "online",
                  0,
                  0,
                  observedAt,
                  expectedSlotResources),
          ],
          expectedTelemetry);
      await store.ApplySyncAsync(
          nodeId,
          "2.0.0",
          observedAt,
          [profile],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);

      var fleet = await store.GetFleetAsync(
          "tenant",
          observedAt,
          TimeSpan.FromMinutes(1),
          cancellationToken);

      await Assert.That(fleet.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles[0].ResourceTelemetry)
          .IsEqualTo(expectedTelemetry);
      await Assert.That(fleet.Nodes[0].Profiles[0].Slots).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles[0].Slots[0].Resources)
          .IsEqualTo(expectedSlotResources);

      var legacyProfile = profile with
      {
        ObservedAt = observedAt.AddSeconds(30),
        Slots =
        [
            profile.Slots[0] with
            {
              Resources = null,
            },
        ],
        ResourceTelemetry = null,
      };
      await store.ApplySyncAsync(
          nodeId,
          "2.0.0",
          legacyProfile.ObservedAt,
          [legacyProfile],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);
      var legacyPayload = JsonNode.Parse(
          JsonSerializer.Serialize(
              legacyProfile,
              PitCrewProtocolJsonContext.Default.ManagerObservedState))?
          .AsObject() ??
          throw new InvalidOperationException(
              "The legacy profile could not be represented as JSON.");
      legacyPayload.Remove("resourceTelemetry");
      foreach (var slot in legacyPayload["slots"]!.AsArray())
      {
        slot!.AsObject().Remove("resources");
      }
      await using (var connection = await connectionFactory.OpenAsync(
          cancellationToken))
      await using (var command = connection.CreateCommand())
      {
        command.CommandText =
            """
            UPDATE profiles
            SET payload_json = $payload
            WHERE node_id = $nodeId
              AND profile_id = 'default';
            """;
        command.Parameters.AddWithValue(
            "$payload",
            legacyPayload.ToJsonString());
        command.Parameters.AddWithValue(
            "$nodeId",
            nodeId.ToString("D"));
        var updatedRows = await command.ExecuteNonQueryAsync(
            cancellationToken);
        await Assert.That(updatedRows).IsEqualTo(1);
      }

      var legacyFleet = await store.GetFleetAsync(
          "tenant",
          legacyProfile.ObservedAt,
          TimeSpan.FromMinutes(1),
          cancellationToken);

      await Assert.That(legacyFleet.Nodes).HasSingleItem();
      await Assert.That(legacyFleet.Nodes[0].Profiles).HasSingleItem();
      await Assert.That(
              legacyFleet.Nodes[0].Profiles[0].ResourceTelemetry)
          .IsNull();
      await Assert.That(legacyFleet.Nodes[0].Profiles[0].Slots)
          .HasSingleItem();
      await Assert.That(
              legacyFleet.Nodes[0].Profiles[0].Slots[0].Resources)
          .IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Display_Name_Migration_Preserves_Existing_Node(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-fleet-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      var nodeId = Guid.NewGuid();
      await CreateVersionThreeDatabaseAsync(
          connectionFactory,
          nodeId,
          cancellationToken);

      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var store = new SqliteFleetStore(connectionFactory);
      var beforeRename = await store.GetFleetAsync(
          "tenant",
          DateTimeOffset.UtcNow,
          TimeSpan.FromMinutes(1),
          cancellationToken);
      var renamed = await store.RenameNodeAsync(
          "tenant",
          nodeId,
          "Operator name",
          cancellationToken);
      var afterRename = await store.GetFleetAsync(
          "tenant",
          DateTimeOffset.UtcNow,
          TimeSpan.FromMinutes(1),
          cancellationToken);

      await Assert.That(beforeRename.Nodes).HasSingleItem();
      await Assert.That(beforeRename.Nodes[0].NodeId)
          .IsEqualTo(nodeId);
      await Assert.That(beforeRename.Nodes[0].DisplayName)
          .IsEqualTo("Connector name");
      await Assert.That(renamed)
          .IsEqualTo(NodeMutationStatus.Succeeded);
      await Assert.That(afterRename.Nodes[0].DisplayName)
          .IsEqualTo("Operator name");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Node_Rename_Persists_Across_Revocation_And_Reenrollment(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-fleet-{Guid.NewGuid():N}.db");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);
      var now = DateTimeOffset.UtcNow;
      var owner = new DashboardUser(
          "1",
          "owner",
          "Owner",
          null);
      await new SqliteAccessStore(connectionFactory)
          .EnsureTenantOwnerAsync(
              "tenant",
              "Tenant",
              owner,
              now,
              cancellationToken);
      var store = new SqliteFleetStore(connectionFactory);
      await CreateEnrollmentCodeAsync(
          store,
          "tenant",
          owner.GitHubUserId,
          "code-one",
          now,
          cancellationToken);
      var firstEnrollment = await store.RedeemEnrollmentCodeAsync(
          "code-one",
          "connector-instance",
          "Connector name",
          "credential-one",
          now,
          cancellationToken);
      var nodeId = firstEnrollment.NodeId ??
          throw new InvalidOperationException(
              "Initial enrollment did not return a node ID.");

      var renamed = await store.RenameNodeAsync(
          "tenant",
          nodeId,
          "Operator name",
          cancellationToken);
      var revoked = await store.RevokeNodeAsync(
          "tenant",
          nodeId,
          now.AddMinutes(1),
          cancellationToken);
      var renamedWhileRevoked = await store.RenameNodeAsync(
          "tenant",
          nodeId,
          "Renamed while revoked",
          cancellationToken);
      await CreateEnrollmentCodeAsync(
          store,
          "tenant",
          owner.GitHubUserId,
          "code-two",
          now.AddMinutes(2),
          cancellationToken);
      var secondEnrollment = await store.RedeemEnrollmentCodeAsync(
          "code-two",
          "connector-instance",
          "Updated connector name",
          "credential-two",
          now.AddMinutes(2),
          cancellationToken);
      var fleet = await store.GetFleetAsync(
          "tenant",
          now.AddMinutes(2),
          TimeSpan.FromMinutes(1),
          cancellationToken);
      var wrongTenant = await store.RenameNodeAsync(
          "other",
          nodeId,
          "Wrong tenant",
          cancellationToken);

      await Assert.That(renamed)
          .IsEqualTo(NodeMutationStatus.Succeeded);
      await Assert.That(revoked)
          .IsEqualTo(NodeMutationStatus.Succeeded);
      await Assert.That(renamedWhileRevoked)
          .IsEqualTo(NodeMutationStatus.Succeeded);
      await Assert.That(wrongTenant)
          .IsEqualTo(NodeMutationStatus.NotFound);
      await Assert.That(secondEnrollment.NodeId)
          .IsEqualTo(nodeId);
      await Assert.That(fleet.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].NodeId)
          .IsEqualTo(nodeId);
      await Assert.That(fleet.Nodes[0].DisplayName)
          .IsEqualTo("Renamed while revoked");
      await Assert.That(fleet.Nodes[0].IsRevoked)
          .IsFalse();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static async Task CreateEnrollmentCodeAsync(
      SqliteFleetStore store,
      string tenantId,
      string ownerGitHubUserId,
      string codeHash,
      DateTimeOffset createdAt,
      CancellationToken cancellationToken) =>
      await store.CreateEnrollmentCodeAsync(
          Guid.NewGuid(),
          tenantId,
          codeHash,
          "Enrollment",
          ownerGitHubUserId,
          createdAt,
          createdAt.AddMinutes(10),
          cancellationToken);

  private static async Task<(
      SqliteConnectionFactory ConnectionFactory,
      SqliteFleetStore Store,
      Guid NodeId)> CreateEnrolledStoreAsync(
      string databasePath,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    var connectionFactory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
        cancellationToken);
    var owner = new DashboardUser(
        "1",
        "owner",
        "Owner",
        null);
    await new SqliteAccessStore(connectionFactory).EnsureTenantOwnerAsync(
        "tenant",
        "Tenant",
        owner,
        now,
        cancellationToken);
    var store = new SqliteFleetStore(connectionFactory);
    const string codeHash = "code-hash";
    await CreateEnrollmentCodeAsync(
        store,
        "tenant",
        owner.GitHubUserId,
        codeHash,
        now,
        cancellationToken);
    var enrollment = await store.RedeemEnrollmentCodeAsync(
        codeHash,
        "connector-instance",
        "Connector name",
        "credential-hash",
        now,
        cancellationToken);
    var nodeId = enrollment.NodeId ??
        throw new InvalidOperationException(
            "Enrollment did not return a node ID.");
    return (connectionFactory, store, nodeId);
  }

  private static async Task CreateVersionThreeDatabaseAsync(
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using (var setupCommand = connection.CreateCommand())
    {
      setupCommand.CommandText =
          """
          CREATE TABLE schema_migrations (
              version INTEGER PRIMARY KEY,
              name TEXT NOT NULL,
              checksum TEXT NOT NULL,
              applied_at TEXT NOT NULL
          );
          """;
      await setupCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    foreach (var migration in SqliteMigrationCatalog.All
        .Where(candidate => candidate.Version <= 3))
    {
      await using var transaction = (SqliteTransaction)
          await connection.BeginTransactionAsync(cancellationToken);
      await using var migrationCommand = connection.CreateCommand();
      migrationCommand.Transaction = transaction;
      migrationCommand.CommandText = migration.Sql;
      await migrationCommand.ExecuteNonQueryAsync(cancellationToken);

      await using var recordCommand = connection.CreateCommand();
      recordCommand.Transaction = transaction;
      recordCommand.CommandText =
          """
          INSERT INTO schema_migrations (
              version,
              name,
              checksum,
              applied_at)
          VALUES (
              $version,
              $name,
              $checksum,
              $appliedAt);
          """;
      recordCommand.Parameters.AddWithValue(
          "$version",
          migration.Version);
      recordCommand.Parameters.AddWithValue(
          "$name",
          migration.Name);
      recordCommand.Parameters.AddWithValue(
          "$checksum",
          migration.Checksum);
      recordCommand.Parameters.AddWithValue(
          "$appliedAt",
          DateTimeOffset.UtcNow.ToString(
              "O",
              CultureInfo.InvariantCulture));
      await recordCommand.ExecuteNonQueryAsync(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
    }

    await using var seedCommand = connection.CreateCommand();
    seedCommand.CommandText =
        """
        INSERT INTO tenants (
            tenant_id,
            display_name,
            created_at)
        VALUES (
            'tenant',
            'Tenant',
            '2026-07-19T00:00:00.0000000+00:00');

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
            'Connector name',
            'credential-hash',
            '2026-07-19T00:00:00.0000000+00:00');
        """;
    seedCommand.Parameters.AddWithValue(
        "$nodeId",
        nodeId.ToString("D"));
    await seedCommand.ExecuteNonQueryAsync(cancellationToken);
  }
}
