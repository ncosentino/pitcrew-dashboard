using Microsoft.Extensions.Hosting;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

[DoNotAutoRegister]
internal sealed class SqliteFleetStoreInitializer(
    IFleetStore _fleetStore) : IHostedLifecycleService
{
  public Task StartingAsync(CancellationToken cancellationToken) =>
      _fleetStore.InitializeAsync(cancellationToken);

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
