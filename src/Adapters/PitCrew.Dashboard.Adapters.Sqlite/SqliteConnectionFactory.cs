using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteConnectionFactory
{
  private readonly IOptions<SqliteFleetStoreOptions> _options;
  private readonly SqliteContentionPolicy _contentionPolicy;

  public SqliteConnectionFactory(
      IOptions<SqliteFleetStoreOptions> options)
      : this(options, TimeProvider.System)
  {
  }

  public SqliteConnectionFactory(
      IOptions<SqliteFleetStoreOptions> options,
      TimeProvider timeProvider)
  {
    _options = options;
    _contentionPolicy = new SqliteContentionPolicy(
        options,
        timeProvider);
  }

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
    command.CommandText = FormattableString.Invariant(
        $"""
         PRAGMA foreign_keys = ON;
         PRAGMA busy_timeout = {_options.Value.BusyTimeoutMilliseconds};
         """);
    await command.ExecuteNonQueryAsync(cancellationToken);
    return connection;
  }

  public async Task<SqliteTransaction> BeginWriteTransactionAsync(
      SqliteConnection connection,
      CancellationToken cancellationToken) =>
      await _contentionPolicy.ExecuteAsync(
          _ => Task.FromResult(connection.BeginTransaction(deferred: false)),
          cancellationToken);

  public async Task ExecuteWithContentionRetryAsync(
      Func<CancellationToken, Task> operation,
      CancellationToken cancellationToken) =>
      await _contentionPolicy.ExecuteAsync(operation, cancellationToken);
}
