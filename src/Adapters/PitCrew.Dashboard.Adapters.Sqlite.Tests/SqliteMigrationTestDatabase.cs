using System.Globalization;

using Microsoft.Data.Sqlite;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

internal static class SqliteMigrationTestDatabase
{
  internal static async Task ApplyThroughAsync(
      SqliteConnectionFactory connectionFactory,
      int maximumVersion,
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
        .Where(candidate => candidate.Version <= maximumVersion))
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
      recordCommand.Parameters.AddWithValue("$version", migration.Version);
      recordCommand.Parameters.AddWithValue("$name", migration.Name);
      recordCommand.Parameters.AddWithValue("$checksum", migration.Checksum);
      recordCommand.Parameters.AddWithValue(
          "$appliedAt",
          DateTimeOffset.UtcNow.ToString(
              "O",
              CultureInfo.InvariantCulture));
      await recordCommand.ExecuteNonQueryAsync(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
    }
  }
}
