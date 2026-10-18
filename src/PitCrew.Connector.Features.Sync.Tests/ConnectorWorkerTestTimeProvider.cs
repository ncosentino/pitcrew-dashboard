using System.Threading.Channels;

using Microsoft.Extensions.Time.Testing;

namespace PitCrew.Connector.Features.Sync.Tests;

internal sealed class ConnectorWorkerTestTimeProvider(DateTimeOffset now) : FakeTimeProvider(now)
{
  private readonly Channel<TimeSpan> _delays = Channel.CreateBounded<TimeSpan>(1);

  public override ITimer CreateTimer(
      TimerCallback callback,
      object? state,
      TimeSpan dueTime,
      TimeSpan period)
  {
    var timer = base.CreateTimer(callback, state, dueTime, period);
    // Journal writes use a one-second durability budget, not a worker retry timer.
    if (dueTime == TimeSpan.FromSeconds(1))
    {
      return timer;
    }
    if (!_delays.Writer.TryWrite(dueTime))
    {
      timer.Dispose();
      throw new InvalidOperationException("The test must consume each worker timer registration before another is scheduled.");
    }
    return timer;
  }

  public ValueTask<TimeSpan> WaitForDelayAsync(CancellationToken cancellationToken) =>
      _delays.Reader.ReadAsync(cancellationToken);
}
