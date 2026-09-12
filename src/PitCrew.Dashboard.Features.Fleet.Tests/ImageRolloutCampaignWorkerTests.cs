using Microsoft.Data.Sqlite;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

[NotInParallel]
public sealed class ImageRolloutCampaignWorkerTests
{
  [Test]
  public async Task Worker_Continues_After_Contention_Exhaustion(
      CancellationToken cancellationToken)
  {
    await using var context =
        await ImageRolloutCampaignWorkerTestContext.CreateAsync(
            "contention",
            cancellationToken);
    await using var blockingConnection =
        await context.ConnectionFactory.OpenAsync(cancellationToken);
    await using var blockingTransaction =
        blockingConnection.BeginTransaction(deferred: false);

    var contended = await context.Worker.ProcessIterationAsync(
        cancellationToken);
    await blockingTransaction.RollbackAsync(cancellationToken);
    var recovered = await context.Worker.ProcessIterationAsync(
        cancellationToken);

    await Assert.That(contended).IsFalse()
        .Because("exhausted contention is a retryable worker outcome");
    await Assert.That(recovered).IsTrue()
        .Because("the next rollout iteration must continue");
    await Assert.That(context.Logger.EntryCount).IsEqualTo(1);
  }

  [Test]
  public async Task Worker_Does_Not_Contain_Or_Log_NonContention_Database_Failure(
      CancellationToken cancellationToken)
  {
    await using var context =
        await ImageRolloutCampaignWorkerTestContext.CreateAsync(
            "database-failure",
            cancellationToken);
    await using var connection =
        await context.ConnectionFactory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "DROP TABLE image_rollout_campaigns;";
    await command.ExecuteNonQueryAsync(cancellationToken);

    await Assert.That(async () =>
            await context.Worker.ProcessIterationAsync(cancellationToken))
        .Throws<SqliteException>();
    await Assert.That(context.Logger.EntryCount).IsEqualTo(0);
  }
}
