namespace PitCrew.Support.Broker.App;

internal sealed class SupportBrokerStartupCoordinator(
    SupportBrokerRuntimeValidator _runtimeValidator,
    SupportBrokerStartupStatusWriter _startupStatus,
    TimeProvider _timeProvider)
{
  private static readonly TimeSpan _retryInterval =
      TimeSpan.FromSeconds(1);
  private static readonly TimeSpan _retryWindow =
      TimeSpan.FromSeconds(60);

  public async Task WaitUntilReadyAsync(
      CancellationToken cancellationToken)
  {
    _startupStatus.Clear();
    var deadline = _timeProvider.GetUtcNow() + _retryWindow;
    while (true)
    {
      var disposition = _runtimeValidator.Validate();
      _startupStatus.Write(disposition);
      if (string.Equals(
          disposition,
          SupportBrokerStartupDispositions.Ready,
          StringComparison.Ordinal))
      {
        return;
      }
      if (_timeProvider.GetUtcNow() >= deadline)
      {
        throw new InvalidOperationException(
            $"PitCrew support broker startup rejected: {disposition}.");
      }
      await Task.Delay(
          _retryInterval,
          _timeProvider,
          cancellationToken);
    }
  }
}
