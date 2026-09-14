namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ConnectorWorkerRetryBehaviorTests
{
  [Test]
  public async Task Immediate_Health_Replay_Requires_Complete_Observation()
  {
    var incompleteObservationReplay =
        ConnectorWorker.ShouldScheduleImmediateHealthReplay(
            observationIsComplete: false,
            replayedActiveOutage: true);
    var completeObservationReplay =
        ConnectorWorker.ShouldScheduleImmediateHealthReplay(
            observationIsComplete: true,
            replayedActiveOutage: true);

    await Assert.That(incompleteObservationReplay)
        .IsFalse()
        .Because(
            "incomplete observations must retain their jittered retry backoff");
    await Assert.That(completeObservationReplay)
        .IsTrue()
        .Because(
            "a completed synchronization must immediately replay its recovery event");
  }
}
