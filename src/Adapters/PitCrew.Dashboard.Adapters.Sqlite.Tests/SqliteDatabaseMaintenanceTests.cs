using Microsoft.Data.Sqlite;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteDatabaseMaintenanceTests
{
  [Test]
  public async Task Backup_Verify_And_Restore_Preserve_Consistent_Data(
      CancellationToken cancellationToken)
  {
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-sqlite-maintenance-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    var sourcePath = Path.Combine(directory, "source.db");
    var backupPath = Path.Combine(directory, "backup.db");
    var destinationPath = Path.Combine(directory, "destination.db");
    try
    {
      await CreateDatabaseAsync(
          sourcePath,
          "source",
          cancellationToken);
      await CreateDatabaseAsync(
          destinationPath,
          "destination",
          cancellationToken);
      var maintenance = new SqliteDatabaseMaintenance();

      var backup = maintenance.Backup(
          sourcePath,
          backupPath,
          cancellationToken);
      var verification = maintenance.Verify(
          backupPath,
          cancellationToken);
      var restore = maintenance.Restore(
          backupPath,
          destinationPath,
          cancellationToken);

      await Assert.That(backup.Succeeded).IsTrue();
      await Assert.That(verification.Succeeded).IsTrue();
      await Assert.That(restore.Succeeded).IsTrue();
      await Assert.That(restore.RollbackPath).IsNotNull();
      await Assert.That(await ReadValueAsync(
              destinationPath,
              cancellationToken))
          .IsEqualTo("source");
      await Assert.That(await ReadValueAsync(
              restore.RollbackPath!,
              cancellationToken))
          .IsEqualTo("destination");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      if (Directory.Exists(directory))
      {
        Directory.Delete(directory, true);
      }
    }
  }

  private static async Task CreateDatabaseAsync(
      string path,
      string value,
      CancellationToken cancellationToken)
  {
    await using var connection = new SqliteConnection(
        $"Data Source={path};Pooling=False");
    await connection.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        PRAGMA journal_mode = WAL;
        CREATE TABLE values_table (
            value TEXT NOT NULL
        );
        INSERT INTO values_table (value)
        VALUES ($value);
        """;
    command.Parameters.AddWithValue("$value", value);
    await command.ExecuteNonQueryAsync(
        cancellationToken);
  }

  private static async Task<string> ReadValueAsync(
      string path,
      CancellationToken cancellationToken)
  {
    await using var connection = new SqliteConnection(
        $"Data Source={path};Mode=ReadOnly;Pooling=False");
    await connection.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT value
        FROM values_table;
        """;
    return Convert.ToString(
        await command.ExecuteScalarAsync(
            cancellationToken),
        System.Globalization.CultureInfo.InvariantCulture) ??
        string.Empty;
  }
}
