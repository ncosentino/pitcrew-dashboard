using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusLabs.Needlr;

using PitCrew.Dashboard.Kernel.ExceptionHandling;
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
        await ProcessIterationAsync(stoppingToken);
      }
      while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
    }
  }

  internal async Task<bool> ProcessIterationAsync(
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    try
    {
      await _processor.ProcessOnceAsync(cancellationToken);
      return true;
    }
    catch (DurableStoreContentionException exception)
    {
      LogStorageFailure(exception);
      return false;
    }
  }

  [LoggerMessage(
      EventId = 1,
      Level = LogLevel.Warning,
      Message = "Image rollout processing could not access durable state and will retry.")]
  private partial void LogStorageFailure(Exception exception);
}
