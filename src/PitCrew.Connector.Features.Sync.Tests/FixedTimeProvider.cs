namespace PitCrew.Connector.Features.Sync.Tests;

internal sealed class FixedTimeProvider(
    DateTimeOffset _now) : TimeProvider
{
  public override DateTimeOffset GetUtcNow() => _now;
}
