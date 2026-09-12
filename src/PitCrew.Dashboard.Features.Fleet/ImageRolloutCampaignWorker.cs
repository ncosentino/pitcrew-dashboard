using System.Data.Common;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusLabs.Needlr;

using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Fleet;

[DoNotAutoRegister]
internal sealed partial class ImageRolloutCampaignWorker(
    IImageRolloutCampaignProcessor _processor,
    IOptions<ImageRolloutCampaignOptions> _options,
    TimeProvider _timeProvider,
    ILogger<ImageRolloutCampaignWorker> _logger) : BackgroundService
{
  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(
        TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds),
        _timeProvider);
    try
    {
      do
      {
        try
        {
          await _processor.ProcessOnceAsync(stoppingToken);
        }
        catch (DbException exception)
        {
          LogStorageFailure(exception);
        }
      }
      while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
    }
  }

  [LoggerMessage(
      EventId = 1,
      Level = LogLevel.Warning,
      Message = "Image rollout processing could not access durable state and will retry.")]
  private partial void LogStorageFailure(Exception exception);
}
