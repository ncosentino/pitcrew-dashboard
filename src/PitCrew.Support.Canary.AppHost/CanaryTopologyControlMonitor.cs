using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.Hosting;

using PitCrew.Support.Canary.Contracts;

namespace PitCrew.Support.Canary.AppHost;

internal sealed class CanaryTopologyControlMonitor(
    CanaryAppHostOptions _options,
    ResourceCommandService _commandService,
    TimeProvider _timeProvider) : BackgroundService
{
  private static readonly TimeSpan _relayOutageDuration =
      TimeSpan.FromSeconds(18);

  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    var requestPath = Path.Combine(
        _options.RunRoot,
        CanaryTopologyControlFile.RequestFileName);
    var resultPath = Path.Combine(
        _options.RunRoot,
        CanaryTopologyControlFile.ResultFileName);
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(100));
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
      if (!File.Exists(requestPath))
      {
        continue;
      }
      var request = CanaryTopologyControlFile.ReadRequest(
          requestPath);
      if (!string.Equals(
              request.RunId,
              _options.RunId,
              StringComparison.Ordinal) ||
          File.Exists(resultPath))
      {
        throw new InvalidDataException(
            "The topology control request does not match the active run.");
      }
      var stop = await _commandService.ExecuteCommandAsync(
          "support-relay",
          KnownResourceCommands.StopCommand,
          stoppingToken);
      if (stop.Success)
      {
        await Task.Delay(
            _relayOutageDuration,
            _timeProvider,
            stoppingToken);
      }
      var start = stop.Success
          ? await _commandService.ExecuteCommandAsync(
              "support-relay",
              KnownResourceCommands.StartCommand,
              stoppingToken)
          : null;
      var succeeded = stop.Success && start?.Success == true;
      CanaryTopologyControlFile.WriteResult(
          resultPath,
          new CanaryTopologyControlResult(
              CanaryTopologyControlFile.SchemaVersion,
              _options.RunId,
              request.RequestId,
              succeeded ? "succeeded" : "failed",
              succeeded
                  ? "restart-command-succeeded"
                  : "restart-command-rejected"));
      File.Delete(requestPath);
    }
  }
}
