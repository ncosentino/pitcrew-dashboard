using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class ConnectorHealthReplayValidationTests
{
  private static readonly DateTimeOffset Now = new(
      2026,
      8,
      7,
      12,
      0,
      0,
      TimeSpan.Zero);

  [Test]
  public async Task Valid_Replay_Is_Accepted()
  {
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidConnectorHealthReplay(
                CreateReplay(),
                Now.AddMinutes(5)))
        .IsTrue();
  }

  [Test]
  public async Task Replay_Rejects_Unsafe_Or_Ambiguous_Evidence()
  {
    var valid = CreateReplay();
    var duplicatedEvent = valid with
    {
      Events =
      [
          valid.Events[0],
          valid.Events[0],
      ],
    };
    var secretDetail = valid with
    {
      Snapshot = valid.Snapshot with
      {
        LastFailureDetail =
            "https://user:password@example.test/?token=secret",
      },
    };
    var invalidProfile = valid with
    {
      Events =
      [
          valid.Events[0] with
          {
            ProfileId = "../secret",
          },
      ],
    };
    var futureEvent = valid with
    {
      Events =
      [
          valid.Events[0] with
          {
            OccurredAt = Now.AddMinutes(6),
          },
      ],
    };
    var emptyEventId = valid with
    {
      Events =
      [
          valid.Events[0] with
          {
            EventId = Guid.Empty,
          },
      ],
    };

    await Assert.That(
            SyncConnectorUnitOfWork.IsValidConnectorHealthReplay(
                duplicatedEvent,
                Now.AddMinutes(5)))
        .IsFalse();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidConnectorHealthReplay(
                secretDetail,
                Now.AddMinutes(5)))
        .IsFalse();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidConnectorHealthReplay(
                invalidProfile,
                Now.AddMinutes(5)))
        .IsFalse();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidConnectorHealthReplay(
                futureEvent,
                Now.AddMinutes(5)))
        .IsFalse();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidConnectorHealthReplay(
                emptyEventId,
                Now.AddMinutes(5)))
        .IsFalse();
  }

  [Test]
  public async Task Replay_Rejects_More_Than_256_Events()
  {
    var valid = CreateReplay();
    var oversized = valid with
    {
      Events = Enumerable.Range(0, 257)
          .Select(index => valid.Events[0] with
          {
            EventId = Guid.NewGuid(),
            ConsecutiveFailures = index,
          })
          .ToArray(),
    };

    await Assert.That(
            SyncConnectorUnitOfWork.IsValidConnectorHealthReplay(
                oversized,
                Now.AddMinutes(5)))
        .IsFalse();
  }

  private static ConnectorHealthReplay CreateReplay()
  {
    var outageId = new Guid(
        "11111111-1111-1111-1111-111111111111");
    return new ConnectorHealthReplay(
        new ConnectorHealthReplaySnapshot(
            "degraded",
            Now.AddHours(-1),
            Now,
            Now,
            Now.AddMinutes(-10),
            outageId,
            Now.AddMinutes(-5),
            Now,
            "synchronization-network",
            "default",
            "Connector synchronization could not reach Dashboard.",
            3,
            Now.AddHours(1),
            null,
            null,
            null,
            null),
        [
            new ConnectorHealthReplayEvent(
                new Guid(
                    "22222222-2222-2222-2222-222222222222"),
                "synchronization-failed",
                Now,
                "degraded",
                outageId,
                Now.AddMinutes(-5),
                "synchronization-network",
                "default",
                3,
                300,
                "Connector synchronization could not reach Dashboard."),
        ]);
  }
}
