using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NexusLabs.Needlr;

using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Fleet;

[DoNotAutoRegister]
internal sealed class ImageRolloutCampaignWorker(
    IImageRolloutCampaignProcessor _processor,
    IOptions<ImageRolloutCampaignOptions> _options,
    TimeProvider _timeProvider) : BackgroundService
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
        await _processor.ProcessOnceAsync(stoppingToken);
      }
      while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
    }
  }

}
