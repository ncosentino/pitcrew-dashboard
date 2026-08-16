using Microsoft.Extensions.Hosting;

namespace PitCrew.Support.Broker.App;

internal sealed class SupportBrokerWorker(
    ISupportBrokerServer _server,
    IHostEnvironment _environment)
    : BackgroundService
{
  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    var statusPath = BrokerStartupStatusWriter.GetPath(
        _environment.ContentRootPath);
    File.Delete(statusPath);
    try
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        await _server.RunOnceAsync(stoppingToken);
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
    }
    catch (Exception exception)
    {
      Environment.ExitCode = 1;
      await BrokerStartupStatusWriter.WriteFailureAsync(
          statusPath,
          exception.GetType().Name,
          CancellationToken.None);
      throw;
    }
  }
}
