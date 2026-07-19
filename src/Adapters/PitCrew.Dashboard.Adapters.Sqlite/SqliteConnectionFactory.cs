using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteConnectionFactory(
    IOptions<SqliteFleetStoreOptions> _options)
{
  public async Task<SqliteConnection> OpenAsync(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.GetFullPath(_options.Value.DatabasePath);
    var directory = Path.GetDirectoryName(databasePath);
    if (string.IsNullOrWhiteSpace(directory))
    {
      throw new InvalidOperationException(
          $"SQLite database path '{databasePath}' does not have a parent directory.");
    }

    Directory.CreateDirectory(directory);
    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = databasePath,
      Mode = SqliteOpenMode.ReadWriteCreate,
      Pooling = true,
    }.ToString();
    var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;
    await command.ExecuteNonQueryAsync(cancellationToken);
    return connection;
  }
}
