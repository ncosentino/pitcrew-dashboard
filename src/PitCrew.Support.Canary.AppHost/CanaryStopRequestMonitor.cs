using Microsoft.Extensions.Hosting;

namespace PitCrew.Support.Canary.AppHost;

internal sealed record CanaryAppHostOptions(
    string RunRoot,
    string RunId);

internal sealed class CanaryStopRequestMonitor(
    CanaryAppHostOptions _options,
    IHostApplicationLifetime _applicationLifetime) : BackgroundService
{
  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    var path = Path.Combine(
        _options.RunRoot,
        "stop.request");
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(250));
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
      if (!File.Exists(path))
      {
        continue;
      }
      string value;
      try
      {
        value = await File.ReadAllTextAsync(
            path,
            stoppingToken);
      }
      catch (IOException)
      {
        continue;
      }
      if (!string.Equals(
          value.Trim(),
          _options.RunId,
          StringComparison.Ordinal))
      {
        continue;
      }
      File.Delete(path);
      _applicationLifetime.StopApplication();
      return;
    }
  }
}
