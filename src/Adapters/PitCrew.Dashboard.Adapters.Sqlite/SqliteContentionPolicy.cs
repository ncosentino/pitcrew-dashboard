using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed class SqliteContentionPolicy(
    IOptions<SqliteFleetStoreOptions> _options,
    TimeProvider _timeProvider)
{
  private const int SqliteBusy = 5;
  private const int SqliteLocked = 6;

  public async Task<T> ExecuteAsync<T>(
      Func<CancellationToken, Task<T>> operation,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(operation);

    for (var attempt = 1; ; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        return await operation(cancellationToken);
      }
      catch (SqliteException exception)
          when (IsRetryable(exception) &&
                attempt < _options.Value.ContentionMaximumAttempts)
      {
        var delay = TimeSpan.FromMilliseconds(
            _options.Value.ContentionRetryDelayMilliseconds * attempt);
        await Task.Delay(delay, _timeProvider, cancellationToken);
      }
    }
  }

  public async Task ExecuteAsync(
      Func<CancellationToken, Task> operation,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(operation);
    await ExecuteAsync(
        async token =>
        {
          await operation(token);
          return true;
        },
        cancellationToken);
  }

  private static bool IsRetryable(SqliteException exception) =>
      exception.SqliteErrorCode is SqliteBusy or SqliteLocked;
}
