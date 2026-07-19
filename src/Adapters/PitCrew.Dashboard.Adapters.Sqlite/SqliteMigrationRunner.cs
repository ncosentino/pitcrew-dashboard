using System.Globalization;

using Microsoft.Data.Sqlite;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteMigrationRunner(
    SqliteConnectionFactory _connectionFactory)
{
  public async Task ApplyAsync(CancellationToken cancellationToken)
  {
    await using var connection = await _connectionFactory.OpenAsync(
        cancellationToken);
    await using (var setupCommand = connection.CreateCommand())
    {
      setupCommand.CommandText =
          """
                PRAGMA journal_mode = WAL;

                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    checksum TEXT NOT NULL,
                    applied_at TEXT NOT NULL
                );
                """;
      await setupCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    var applied = new Dictionary<int, AppliedMigration>();
    await using (var queryCommand = connection.CreateCommand())
    {
      queryCommand.CommandText =
          """
                SELECT version, name, checksum
                FROM schema_migrations
                ORDER BY version;
                """;
      await using var reader = await queryCommand.ExecuteReaderAsync(
          cancellationToken);
      while (await reader.ReadAsync(cancellationToken))
      {
        applied.Add(
            reader.GetInt32(0),
            new AppliedMigration(
                reader.GetString(1),
                reader.GetString(2)));
      }
    }

    var knownVersions = SqliteMigrationCatalog.All
        .Select(migration => migration.Version)
        .ToHashSet();
    var unknownVersion = applied.Keys
        .Where(version => !knownVersions.Contains(version))
        .Order()
        .FirstOrDefault();
    if (unknownVersion != 0)
    {
      throw new InvalidOperationException(
          $"SQLite schema version '{unknownVersion}' is newer than this dashboard binary.");
    }

    var expectedVersion = 1;
    foreach (var migration in SqliteMigrationCatalog.All)
    {
      if (migration.Version != expectedVersion)
      {
        throw new InvalidOperationException(
            $"SQLite migration catalog has a gap before version '{migration.Version}'.");
      }
      expectedVersion++;

      if (applied.TryGetValue(
          migration.Version,
          out var appliedMigration))
      {
        if (!string.Equals(
            migration.Name,
            appliedMigration.Name,
            StringComparison.Ordinal) ||
            !string.Equals(
                migration.Checksum,
                appliedMigration.Checksum,
                StringComparison.Ordinal))
        {
          throw new InvalidOperationException(
              $"SQLite migration '{migration.Version}' no longer matches the applied schema.");
        }
        continue;
      }

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
  }

  private sealed record AppliedMigration(
      string Name,
      string Checksum);
}
