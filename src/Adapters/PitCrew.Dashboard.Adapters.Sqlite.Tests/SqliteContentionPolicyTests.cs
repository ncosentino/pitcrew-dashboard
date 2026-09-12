using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Kernel.ExceptionHandling;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteContentionPolicyTests
{
  [Test]
  [Arguments(5)]
  [Arguments(6)]
  public async Task Retryable_Contention_Succeeds_Within_Bound(
      int errorCode,
      CancellationToken cancellationToken)
  {
    const int expectedValue = 42;
    var attempts = 0;
    var policy = new SqliteContentionPolicy(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = "unused.db",
          ContentionMaximumAttempts = 3,
          ContentionRetryDelayMilliseconds = 0,
        }),
        TimeProvider.System);

    var result = await policy.ExecuteAsync(
        _ =>
        {
          attempts++;
          if (attempts < 3)
          {
            throw new SqliteException("contention", errorCode);
          }

          return Task.FromResult(expectedValue);
        },
        cancellationToken);

    await Assert.That(result).IsEqualTo(expectedValue);
    await Assert.That(attempts).IsEqualTo(3);
  }

  [Test]
  [Arguments(5)]
  [Arguments(6)]
  public async Task Retryable_Contention_Stops_At_Maximum_Attempts(
      int errorCode,
      CancellationToken cancellationToken)
  {
    var attempts = 0;
    var providerException = new SqliteException("contention", errorCode);
    var policy = new SqliteContentionPolicy(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = "unused.db",
          ContentionMaximumAttempts = 3,
          ContentionRetryDelayMilliseconds = 0,
        }),
        TimeProvider.System);

    var exception = await Assert.That(async () =>
            await policy.ExecuteAsync<int>(
                _ =>
                {
                  attempts++;
                  throw providerException;
                },
                cancellationToken))
        .Throws<DurableStoreContentionException>();
    await Assert.That(attempts).IsEqualTo(3);
    await Assert.That(exception!.InnerException)
        .IsEqualTo(providerException);
  }

  [Test]
  public async Task NonContention_Database_Failure_Is_Not_Retried(
      CancellationToken cancellationToken)
  {
    var attempts = 0;
    var providerException = new SqliteException("constraint", 19);
    var policy = new SqliteContentionPolicy(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = "unused.db",
          ContentionMaximumAttempts = 3,
          ContentionRetryDelayMilliseconds = 0,
        }),
        TimeProvider.System);

    var exception = await Assert.That(async () =>
            await policy.ExecuteAsync<int>(
                _ =>
                {
                  attempts++;
                  throw providerException;
                },
                cancellationToken))
        .Throws<SqliteException>();
    await Assert.That(attempts).IsEqualTo(1);
    await Assert.That(exception).IsEqualTo(providerException);
  }

  [Test]
  public async Task Cancellation_Stops_Contention_Backoff(
      CancellationToken cancellationToken)
  {
    var attempts = 0;
    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken);
    var policy = new SqliteContentionPolicy(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = "unused.db",
          ContentionMaximumAttempts = 3,
          ContentionRetryDelayMilliseconds = 1_000,
        }),
        TimeProvider.System);

    await Assert.That(async () =>
            await policy.ExecuteAsync<int>(
                async _ =>
                {
                  attempts++;
                  await cancellation.CancelAsync();
                  throw new SqliteException("contention", 5);
                },
                cancellation.Token))
        .Throws<OperationCanceledException>();
    await Assert.That(attempts).IsEqualTo(1);
  }
}
