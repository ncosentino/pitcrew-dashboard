using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Adapters.Sqlite;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

internal sealed class ImageRolloutCampaignWorkerTestContext : IAsyncDisposable
{
  private ImageRolloutCampaignWorkerTestContext(
      string databasePath,
      SqliteConnectionFactory connectionFactory,
      ImageRolloutCampaignWorker worker,
      RecordingLogger<ImageRolloutCampaignWorker> logger)
  {
    DatabasePath = databasePath;
    ConnectionFactory = connectionFactory;
    Worker = worker;
    Logger = logger;
  }

  public string DatabasePath { get; }

  public SqliteConnectionFactory ConnectionFactory { get; }

  public ImageRolloutCampaignWorker Worker { get; }

  public RecordingLogger<ImageRolloutCampaignWorker> Logger { get; }

  public static async Task<ImageRolloutCampaignWorkerTestContext> CreateAsync(
      string scope,
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-rollout-worker-{scope}-{Guid.NewGuid():N}.db");
    var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
        new DateTimeOffset(
            2026,
            9,
            12,
            18,
            30,
            0,
            TimeSpan.Zero));
    var connectionFactory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
          BusyTimeoutMilliseconds = 1,
          ContentionMaximumAttempts = 1,
          ContentionRetryDelayMilliseconds = 0,
        }),
        timeProvider);
    await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
        cancellationToken);
    var campaignOptions = Options.Create(
        new ImageRolloutCampaignOptions());
    var processor = new ImageRolloutCampaignProcessor(
        new SqliteImageRolloutCampaignStore(connectionFactory),
        new SqliteImageRolloutCommandStore(connectionFactory),
        Options.Create(new FleetDashboardOptions()),
        campaignOptions,
        timeProvider);
    var logger = new RecordingLogger<ImageRolloutCampaignWorker>();
    var worker = new ImageRolloutCampaignWorker(
        processor,
        campaignOptions,
        timeProvider,
        logger);
    return new ImageRolloutCampaignWorkerTestContext(
        databasePath,
        connectionFactory,
        worker,
        logger);
  }

  public ValueTask DisposeAsync()
  {
    SqliteConnection.ClearAllPools();
    foreach (var path in new[]
    {
        DatabasePath,
        $"{DatabasePath}-shm",
        $"{DatabasePath}-wal",
    })
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }

    return ValueTask.CompletedTask;
  }
}
