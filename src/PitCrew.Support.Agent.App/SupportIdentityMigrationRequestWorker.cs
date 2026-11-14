using System.Text.Json;

using Microsoft.Extensions.Hosting;

namespace PitCrew.Support.Agent.App;

internal sealed class SupportIdentityMigrationRequestWorker(
    SupportIdentityMigration _migration,
    SupportAgentStartupStatusWriter _status,
    IHostEnvironment _environment,
    IHostApplicationLifetime _lifetime) : BackgroundService
{
  internal static bool IsRequestPresent(string contentRoot) =>
      File.Exists(Path.Combine(contentRoot, SupportIdentityMigration.RequestFileName));

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!IsRequestPresent(_environment.ContentRootPath))
    {
      return;
    }
    var disposition = "request-invalid";
    Type? exceptionType = null;
    try
    {
      var path = Path.Combine(
          _environment.ContentRootPath, SupportIdentityMigration.RequestFileName);
      var file = new FileInfo(path);
      if (file.Length is > 0 and <= 256 &&
          (file.Attributes & FileAttributes.ReparsePoint) == 0)
      {
        await using var stream = File.OpenRead(path);
        var command = await JsonSerializer.DeserializeAsync<Command>(
            stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
              UnmappedMemberHandling =
                  System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
            }, stoppingToken);
        if (command is { SchemaVersion: 1 })
        {
          disposition = await _migration.ExecuteAsync(
              command.Operation, stoppingToken);
        }
      }
      File.Delete(path);
    }
    catch (Exception exception)
        when (exception is not OperationCanceledException ||
              !stoppingToken.IsCancellationRequested)
    {
      disposition = "failed";
      exceptionType = exception.GetType();
    }
    _status.Write("identity-migration", disposition, exceptionType);
    _lifetime.StopApplication();
  }

  private sealed record Command(int SchemaVersion, string Operation);
}
