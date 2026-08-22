using System.Text.Json;

using Microsoft.Extensions.Hosting;

namespace PitCrew.Support.Agent.App;

internal sealed class SupportIdentityDeletionRequestWorker(
    SupportNodeIdentityManager _identityManager,
    SupportAgentStartupStatusWriter _startupStatus,
    IHostEnvironment _environment,
    IHostApplicationLifetime _applicationLifetime,
    TimeProvider _timeProvider) : BackgroundService
{
  private const int SchemaVersion = 1;
  private const int MaximumRequestBytes = 256;
  private const string Operation = "delete-keys";
  private const string RequestFileName = "identity-delete-request.json";
  private static readonly JsonSerializerOptions _jsonOptions =
      new(JsonSerializerDefaults.Web);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(
        TimeSpan.FromSeconds(1),
        _timeProvider);
    do
    {
      if (await ProcessRequestIfPresentAsync(stoppingToken))
      {
        return;
      }
    }
    while (await timer.WaitForNextTickAsync(stoppingToken));
  }

  private async Task<bool> ProcessRequestIfPresentAsync(
      CancellationToken cancellationToken)
  {
    var requestPath = Path.Combine(
        _environment.ContentRootPath,
        RequestFileName);
    if (!File.Exists(requestPath))
    {
      return false;
    }
    var disposition = "delete-request-invalid";
    Type? exceptionType = null;
    try
    {
      var file = new FileInfo(requestPath);
      SupportIdentityDeletionRequest? request = null;
      if (file.Length is > 0 and <= MaximumRequestBytes)
      {
        await using var stream = new FileStream(
            requestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        request = await JsonSerializer.DeserializeAsync<SupportIdentityDeletionRequest>(
            stream,
            _jsonOptions,
            cancellationToken: cancellationToken);
      }
      File.Delete(requestPath);
      if (request is
          {
            SchemaVersion: SchemaVersion,
            Operation: Operation,
          })
      {
        await using var operationLock =
            await _identityManager.Store.AcquireOperationLockAsync(
                cancellationToken);
        var removed = await _identityManager.RemoveAsync(
            SupportIdentityKeyRemovalChoice.DeleteKeys,
            cancellationToken);
        disposition = removed
            ? "delete-keys-succeeded"
            : "delete-keys-unavailable";
      }
    }
    catch (Exception exception)
        when (exception is not OperationCanceledException ||
              !cancellationToken.IsCancellationRequested)
    {
      disposition = "delete-keys-failed";
      exceptionType = exception.GetType();
    }
    _startupStatus.Write(
        "identity-removal",
        disposition,
        exceptionType);
    _applicationLifetime.StopApplication();
    return true;
  }
}
