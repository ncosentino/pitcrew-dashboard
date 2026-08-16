using System.Data.Common;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class SupportRelayCleanupWorker(
    SupportRelayCleanupProcessor _processor,
    IOptions<SupportPlaneOptions> _options,
    TimeProvider _timeProvider,
    ILogger<SupportRelayCleanupWorker> _logger) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    try
    {
      await _processor.ProcessDueAsync(stoppingToken);
    }
    catch (DbException)
    {
      LogRetry();
    }
    using var timer = new PeriodicTimer(
        TimeSpan.FromSeconds(
            _options.Value.RelayCleanupIntervalSeconds),
        _timeProvider);
    try
    {
      while (await timer.WaitForNextTickAsync(stoppingToken))
      {
        try
        {
          await _processor.ProcessDueAsync(stoppingToken);
        }
        catch (DbException)
        {
          LogRetry();
        }
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
    }
  }

  private void LogRetry() =>
      _logger.LogWarning(
          "Relay cleanup maintenance could not access durable state and will retry.");
}
