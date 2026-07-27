namespace PitCrew.Connector.Features.Sync.Tests;

internal sealed class FakeHostExecutionEnvironment(
    bool _isContainer) : IHostExecutionEnvironment
{
  public bool IsContainer => _isContainer;
}
