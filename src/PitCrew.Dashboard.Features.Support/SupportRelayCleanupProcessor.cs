using PitCrew.Dashboard.Features.Support.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class SupportRelayCleanupProcessor(
    ISupportStore _supportStore,
    SupportRelayManagementClient _relayClient,
    TimeProvider _timeProvider)
{
  private static readonly TimeSpan _leaseLifetime = TimeSpan.FromMinutes(2);
  private static readonly TimeSpan _maximumBackoff = TimeSpan.FromHours(1);

  public async Task ProcessDueAsync(CancellationToken cancellationToken)
  {
    if (!_relayClient.IsConfigured)
    {
      return;
    }
    var now = _timeProvider.GetUtcNow();
    var leaseId = Guid.NewGuid();
    var cleanup = await _supportStore.ClaimRelayCleanupAsync(
        now,
        leaseId,
        now.Add(_leaseLifetime),
        limit: 16,
        cancellationToken);
    foreach (var item in cleanup)
    {
      await ProcessClaimedAsync(
          item.NodeId,
          item.LeaseId,
          item.AttemptCount,
          cancellationToken);
    }
  }

  public async Task ProcessOwnedAsync(
      Guid nodeId,
      Guid leaseId,
      CancellationToken cancellationToken)
  {
    if (!_relayClient.IsConfigured)
    {
      return;
    }
    if (!await _supportStore.RecordRelayCleanupAttemptAsync(
            nodeId,
            leaseId,
            _timeProvider.GetUtcNow(),
            cancellationToken))
    {
      return;
    }
    await ProcessClaimedAsync(
        nodeId,
        leaseId,
        attemptCount: 1,
        cancellationToken);
  }

  private async Task ProcessClaimedAsync(
      Guid nodeId,
      Guid leaseId,
      int attemptCount,
      CancellationToken cancellationToken)
  {
    SupportRelayManagementStatus status;
    try
    {
      status = await _relayClient.RevokeNodeAsync(
          nodeId,
          cancellationToken);
    }
    catch (HttpRequestException)
    {
      status = SupportRelayManagementStatus.Failed;
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      status = SupportRelayManagementStatus.Failed;
    }
    if (status == SupportRelayManagementStatus.Succeeded)
    {
      await _supportStore.CompleteRelayCleanupAsync(
          nodeId,
          leaseId,
          cancellationToken);
      return;
    }
    await _supportStore.DeferRelayCleanupAsync(
        nodeId,
        leaseId,
        _timeProvider.GetUtcNow().Add(CalculateBackoff(attemptCount)),
        cancellationToken);
  }

  private static TimeSpan CalculateBackoff(int attemptCount)
  {
    var exponent = Math.Clamp(attemptCount - 1, 0, 7);
    var delay = TimeSpan.FromSeconds(30 * Math.Pow(2, exponent));
    return delay <= _maximumBackoff ? delay : _maximumBackoff;
  }
}
