using System.Text.Json;

using Microsoft.Extensions.Hosting;

namespace PitCrew.Support.Broker.App;

internal sealed class SupportBrokerWorker(
    ISupportBrokerServer _server,
    IHostEnvironment _environment)
    : BackgroundService
{
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

  protected override async Task ExecuteAsync(
      CancellationToken stoppingToken)
  {
    var statusPath = Path.Combine(
        _environment.ContentRootPath,
        "broker-startup-status.json");
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
      await WriteFailureStatusAsync(
          statusPath,
          exception.GetType().Name,
          CancellationToken.None);
      throw;
    }
  }

  private static async Task WriteFailureStatusAsync(
      string path,
      string exceptionType,
      CancellationToken cancellationToken)
  {
    var temporaryPath = $"{path}.{Guid.NewGuid():N}.new";
    await using (var stream = new FileStream(
        temporaryPath,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None,
        4096,
        FileOptions.Asynchronous | FileOptions.WriteThrough))
    {
      await JsonSerializer.SerializeAsync(
          stream,
          new BrokerStartupStatus(1, exceptionType),
          _jsonOptions,
          cancellationToken);
      await stream.FlushAsync(cancellationToken);
    }
    File.Move(temporaryPath, path, overwrite: true);
  }

  private sealed record BrokerStartupStatus(
      int SchemaVersion,
      string ExceptionType);
}
