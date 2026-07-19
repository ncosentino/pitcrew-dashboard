using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteFleetStoreTests
{
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
