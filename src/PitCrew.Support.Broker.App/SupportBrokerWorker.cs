using Microsoft.Extensions.Hosting;

namespace PitCrew.Support.Broker.App;

internal sealed class SupportBrokerWorker(
    ISupportBrokerServer _server,
    SupportBrokerStartupCoordinator _startupCoordinator)
    : BackgroundService
{
  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    await _startupCoordinator.WaitUntilReadyAsync(
        stoppingToken);
    while (!stoppingToken.IsCancellationRequested)
    {
      await _server.RunOnceAsync(stoppingToken);
    }
  }
}
