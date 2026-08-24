using System.Text.Json;

using Microsoft.Extensions.Hosting;

namespace PitCrew.Support.Agent.App;

internal sealed class SupportEnrollmentFinalizationRequestWorker(
    SupportNodeIdentityManager _identityManager,
    SupportAgentStartupStatusWriter _startupStatus,
    IHostEnvironment _environment,
    IHostApplicationLifetime _applicationLifetime,
    TimeProvider _timeProvider) : BackgroundService
{
  private const int SchemaVersion = 1;
  private const int MaximumRequestBytes = 256;
  internal const string FinalizeOperation = "finalize-enrollment";
  internal const string RollbackOperation = "rollback-enrollment";
  internal const string RequestFileName =
      "enrollment-finalization-request.json";
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

  internal static bool IsRequestPresent(string contentRootPath) =>
      File.Exists(Path.Combine(contentRootPath, RequestFileName));

  internal async Task<bool> ProcessRequestIfPresentAsync(
      CancellationToken cancellationToken)
  {
    var requestPath = Path.Combine(
        _environment.ContentRootPath,
        RequestFileName);
    if (!File.Exists(requestPath))
    {
      return false;
    }

    var disposition = "request-invalid";
    Type? exceptionType = null;
    try
    {
      var request = await ReadRequestAsync(
          requestPath,
          cancellationToken);
      File.Delete(requestPath);
      disposition = request?.Operation switch
      {
        FinalizeOperation => await FinalizeAsync(cancellationToken),
        RollbackOperation => await RollbackAsync(cancellationToken),
        _ => disposition,
      };
    }
    catch (Exception exception)
        when (exception is not OperationCanceledException ||
              !cancellationToken.IsCancellationRequested)
    {
      disposition = "failed";
      exceptionType = exception.GetType();
    }

    _startupStatus.Write(
        "enrollment-finalization",
        disposition,
        exceptionType);
    _applicationLifetime.StopApplication();
    return true;
  }

  private async Task<string> FinalizeAsync(
      CancellationToken cancellationToken)
  {
    await using var operationLock =
        await _identityManager.Store.AcquireOperationLockAsync(
            cancellationToken);
    var activeIdentity = await _identityManager.Store.LoadActiveAsync(
        cancellationToken);
    if (activeIdentity is null)
    {
      return "active-identity-unavailable";
    }

    return SupportAgentSettingsFinalizer.FinalizeWithBackup(
        _environment.ContentRootPath) switch
    {
      SupportEnrollmentFinalizationStatus.Succeeded => "succeeded",
      SupportEnrollmentFinalizationStatus.AlreadyFinalized =>
          "already-finalized",
      SupportEnrollmentFinalizationStatus.RollbackRequired =>
          "rollback-required",
      SupportEnrollmentFinalizationStatus.SettingsInvalid =>
          "settings-invalid",
      _ => "active-identity-unavailable",
    };
  }

  private async Task<string> RollbackAsync(
      CancellationToken cancellationToken)
  {
    await using var operationLock =
        await _identityManager.Store.AcquireOperationLockAsync(
            cancellationToken);
    return SupportAgentSettingsFinalizer.Rollback(
        _environment.ContentRootPath) switch
    {
      SupportEnrollmentRollbackStatus.Succeeded => "rollback-succeeded",
      SupportEnrollmentRollbackStatus.BackupUnavailable =>
          "rollback-unavailable",
      _ => "rollback-failed",
    };
  }

  private static async Task<SupportEnrollmentFinalizationCommand?> ReadRequestAsync(
      string requestPath,
      CancellationToken cancellationToken)
  {
    var file = new FileInfo(requestPath);
    if (file.Length is <= 0 or > MaximumRequestBytes ||
        (file.Attributes & FileAttributes.ReparsePoint) != 0)
    {
      return null;
    }
    await using var stream = new FileStream(
        requestPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        4096,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    var request =
        await JsonSerializer.DeserializeAsync<SupportEnrollmentFinalizationCommand>(
            stream,
            _jsonOptions,
            cancellationToken: cancellationToken);
    return request is { SchemaVersion: SchemaVersion }
        ? request
        : null;
  }

  private sealed record SupportEnrollmentFinalizationCommand(
      int SchemaVersion,
      string Operation);
}
