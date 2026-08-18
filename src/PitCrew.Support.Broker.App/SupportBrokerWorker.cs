using Microsoft.Extensions.Hosting;

namespace PitCrew.Support.Broker.App;

internal sealed class SupportBrokerWorker(ISupportBrokerServer _server)
    : BackgroundService
{
  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      await _server.RunOnceAsync(stoppingToken);
    }
  }
}
