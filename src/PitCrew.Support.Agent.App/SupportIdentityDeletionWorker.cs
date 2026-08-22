using Microsoft.Extensions.Hosting;

namespace PitCrew.Support.Agent.App;

internal sealed class SupportIdentityDeletionWorker(
    SupportNodeIdentityManager _identityManager,
    SupportAgentStartupStatusWriter _startupStatus,
    IHostApplicationLifetime _applicationLifetime) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var removed = false;
    Exception? failure = null;
    try
    {
      removed = await _identityManager.RemoveAsync(
          SupportIdentityKeyRemovalChoice.DeleteKeys,
          stoppingToken);
    }
    catch (Exception exception)
        when (exception is not OperationCanceledException ||
              !stoppingToken.IsCancellationRequested)
    {
      failure = exception;
    }
    _startupStatus.Write(
        "identity-removal",
        failure is not null
            ? "delete-keys-failed"
            : removed
                ? "delete-keys-succeeded"
                : "delete-keys-unavailable",
        failure?.GetType());
    Environment.ExitCode = removed && failure is null ? 0 : 1;
    _applicationLifetime.StopApplication();
  }
}
