using Microsoft.Extensions.Hosting;

using NexusLabs.Needlr;
namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed class SqliteFleetStoreInitializer(
    SqliteMigrationRunner _migrationRunner) : IHostedLifecycleService
{
  public Task StartingAsync(CancellationToken cancellationToken) =>
      _migrationRunner.ApplyAsync(cancellationToken);

  public Task StartAsync(CancellationToken cancellationToken) =>
      Task.CompletedTask;

  public Task StartedAsync(CancellationToken cancellationToken) =>
      Task.CompletedTask;

  public Task StoppingAsync(CancellationToken cancellationToken) =>
      Task.CompletedTask;

  public Task StopAsync(CancellationToken cancellationToken) =>
      Task.CompletedTask;

  public Task StoppedAsync(CancellationToken cancellationToken) =>
      Task.CompletedTask;
}
