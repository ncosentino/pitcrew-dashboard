namespace PitCrew.Support.Broker.App;

internal interface ISupportBrokerServer : IDisposable
{
  Task RunOnceAsync(CancellationToken cancellationToken);
}
