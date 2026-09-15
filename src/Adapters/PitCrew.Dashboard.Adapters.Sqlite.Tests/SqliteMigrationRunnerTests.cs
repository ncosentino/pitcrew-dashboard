using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteMigrationRunnerTests
{
  private static readonly string[] ActionableIncidentObjects =
  [
      "actionable_incident_projection_versions",
      "actionable_incident_series",
      "actionable_incident_episodes",
      "ix_actionable_incident_open_series",
      "ix_actionable_incident_tenant_status",
      "actionable_incident_memberships",
      "actionable_incident_expiry_locators",
      "ix_actionable_incident_expiry_locator_retention",
      "ix_actionable_incident_expiry_locator_expiry",
      "trg_actionable_incident_episode_insert_version",
      "trg_actionable_incident_episode_delete_version",
      "trg_actionable_incident_episode_update_version",
  ];

  [Test]
  public async Task Upgrade_Repairs_Migration_Thirty_Four_Objects_Missing_From_Applied_Ledger(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-migration-34-repair-{Guid.NewGuid():N}.db");
    try
    {
      var factory = CreateFactory(databasePath);
      await SqliteMigrationTestDatabase.ApplyThroughAsync(
          factory,
          36,
          cancellationToken);
      await DropActionableIncidentObjectsAsync(factory, cancellationToken);

      await new SqliteMigrationRunner(factory).ApplyAsync(cancellationToken);

      await Assert.That(await ReadSchemaObjectNamesAsync(
          factory,
          cancellationToken)).IsEquivalentTo(ActionableIncidentObjects);
      await Assert.That(await ReadLatestMigrationVersionAsync(
          factory,
          cancellationToken)).IsEqualTo(37);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Startup_Rejects_Applied_Migration_When_Required_Object_Is_Missing(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-migration-contract-{Guid.NewGuid():N}.db");
    try
    {
      var factory = CreateFactory(databasePath);
      var runner = new SqliteMigrationRunner(factory);
      await runner.ApplyAsync(cancellationToken);
      await ExecuteAsync(
          factory,
          "DROP TRIGGER trg_actionable_incident_episode_update_version;",
          cancellationToken);

      await Assert.That(async () =>
              await runner.ApplyAsync(cancellationToken))
          .Throws<InvalidOperationException>()
          .WithMessageContaining(
              "trg_actionable_incident_episode_update_version");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static SqliteConnectionFactory CreateFactory(string databasePath) =>
      new(Options.Create(new SqliteFleetStoreOptions
      {
        DatabasePath = databasePath,
      }));

  private static async Task DropActionableIncidentObjectsAsync(
      SqliteConnectionFactory factory,
      CancellationToken cancellationToken) =>
      await ExecuteAsync(
          factory,
          """
          DROP TRIGGER trg_actionable_incident_episode_insert_version;
          DROP TRIGGER trg_actionable_incident_episode_delete_version;
          DROP TRIGGER trg_actionable_incident_episode_update_version;
          DROP TABLE actionable_incident_memberships;
          DROP TABLE actionable_incident_expiry_locators;
          DROP TABLE actionable_incident_episodes;
          DROP TABLE actionable_incident_series;
          DROP TABLE actionable_incident_projection_versions;
          """,
          cancellationToken);

  private static async Task ExecuteAsync(
      SqliteConnectionFactory factory,
      string sql,
      CancellationToken cancellationToken)
  {
    await using var connection = await factory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<IReadOnlyList<string>> ReadSchemaObjectNamesAsync(
      SqliteConnectionFactory factory,
      CancellationToken cancellationToken)
  {
    await using var connection = await factory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        $"""
        SELECT name
        FROM sqlite_master
        WHERE name IN ({string.Join(", ", ActionableIncidentObjects.Select(
            (_, index) => $"$name{index}"))})
        ORDER BY name;
        """;
    for (var index = 0; index < ActionableIncidentObjects.Length; index++)
    {
      command.Parameters.AddWithValue(
          $"$name{index}",
          ActionableIncidentObjects[index]);
    }

    var names = new List<string>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      names.Add(reader.GetString(0));
    }
    return names;
  }

  private static async Task<long> ReadLatestMigrationVersionAsync(
      SqliteConnectionFactory factory,
      CancellationToken cancellationToken)
  {
    await using var connection = await factory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
    return Convert.ToInt64(
        await command.ExecuteScalarAsync(cancellationToken),
        System.Globalization.CultureInfo.InvariantCulture);
  }
}
