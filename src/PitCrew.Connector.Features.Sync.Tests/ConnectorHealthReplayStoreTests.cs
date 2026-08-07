using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ConnectorHealthReplayStoreTests
{
  [Test]
  public async Task Replay_Is_Redelivered_Until_Acknowledged_And_Survives_Restart(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var journal = CreateJournal(root);
      var replayStore = CreateReplayStore(root);
      var startedAt = new DateTimeOffset(
          2026,
          8,
          7,
          10,
          0,
          0,
          TimeSpan.Zero);
      await journal.RecordProcessStartedAsync(
          startedAt,
          cancellationToken);
      await journal.RecordFailureAsync(
          ConnectorHealthEventKinds.SynchronizationFailed,
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.SynchronizationNetwork,
              "ignored"),
          2,
          TimeSpan.FromSeconds(30),
          startedAt.AddMinutes(1),
          cancellationToken);

      var first = await replayStore.ReadPendingAsync(
          cancellationToken);
      var redelivered = await replayStore.ReadPendingAsync(
          cancellationToken);

      await Assert.That(first.Replay).IsNotNull();
      await Assert.That(first.HasPendingEvents).IsTrue();
      await Assert.That(first.RequiresSynchronization).IsTrue();
      await Assert.That(first.Replay!.Snapshot.State)
          .IsEqualTo(ConnectorHealthStates.Degraded);
      await Assert.That(first.Replay.Events).Count().IsEqualTo(2);
      await Assert.That(redelivered.Replay!.Events.Select(
              entry => entry.EventId))
          .IsEquivalentTo(first.Replay.Events.Select(
              entry => entry.EventId));

      await replayStore.AcknowledgeAsync(
          first.Replay.Events
              .Select(entry => entry.EventId)
              .ToArray(),
          cancellationToken);
      var restarted = CreateReplayStore(root);
      var afterRestart = await restarted.ReadPendingAsync(
          cancellationToken);

      await Assert.That(afterRestart.Replay).IsNotNull();
      await Assert.That(afterRestart.HasPendingEvents).IsFalse();
      await Assert.That(afterRestart.RequiresSynchronization).IsFalse();
      await Assert.That(afterRestart.Replay!.Events).IsEmpty();
      await Assert.That(afterRestart.Replay.Snapshot.ActiveOutageId)
          .IsEqualTo(first.Replay.Snapshot.ActiveOutageId);

      await journal.RecordSynchronizationSucceededAsync(
          startedAt.AddMinutes(2),
          cancellationToken);
      var recovered = await restarted.ReadPendingAsync(
          cancellationToken);

      await Assert.That(recovered.HasPendingEvents).IsTrue();
      await Assert.That(recovered.RequiresSynchronization).IsFalse();
      await Assert.That(recovered.Replay!.Events).HasSingleItem();
      await Assert.That(recovered.Replay.Events[0].Kind)
          .IsEqualTo(ConnectorHealthEventKinds.Recovered);
      await Assert.That(recovered.Replay.Snapshot.State)
          .IsEqualTo(ConnectorHealthStates.Healthy);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Missing_Journal_Is_Compatible_And_Malformed_Acknowledgement_Redelivers(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var replayStore = CreateReplayStore(root);
      var missing = await replayStore.ReadPendingAsync(
          cancellationToken);
      await Assert.That(missing.Replay).IsNull();
      await Assert.That(missing.HasPendingEvents).IsFalse();
      await Assert.That(missing.RequiresSynchronization).IsFalse();

      var journal = CreateJournal(root);
      await journal.RecordProcessStartedAsync(
          new DateTimeOffset(
              2026,
              8,
              7,
              11,
              0,
              0,
              TimeSpan.Zero),
          cancellationToken);
      var healthDirectory = Path.Combine(root, "health");
      await File.WriteAllTextAsync(
          Path.Combine(
              healthDirectory,
              "connector-health-acknowledgement.json"),
          "{",
          cancellationToken);

      var malformed = await replayStore.ReadPendingAsync(
          cancellationToken);

      await Assert.That(malformed.Replay).IsNotNull();
      await Assert.That(malformed.HasPendingEvents).IsTrue();
      await Assert.That(malformed.RequiresSynchronization).IsFalse();
      await Assert.That(malformed.Replay!.Events).HasSingleItem();

      var eventsPath = Path.Combine(
          healthDirectory,
          "connector-events.jsonl");
      var validEvent = await File.ReadAllTextAsync(
          eventsPath,
          cancellationToken);
      var invalidEvent = validEvent
          .Replace(
              malformed.Replay.Events[0].EventId.ToString("D"),
              Guid.NewGuid().ToString("D"),
              StringComparison.Ordinal)
          .Replace(
              "\"state\":\"starting\"",
              "\"state\":\"unsafe\"",
              StringComparison.Ordinal);
      await File.AppendAllTextAsync(
          eventsPath,
          invalidEvent,
          cancellationToken);
      var filtered = await replayStore.ReadPendingAsync(
          cancellationToken);

      await Assert.That(filtered.Replay).IsNotNull();
      await Assert.That(filtered.Replay!.Events).HasSingleItem();
      await Assert.That(filtered.Replay.Events[0].EventId)
          .IsEqualTo(malformed.Replay.Events[0].EventId);

      var snapshotPath = Path.Combine(
          healthDirectory,
          "connector-health.json");
      var snapshotJson = await File.ReadAllTextAsync(
          snapshotPath,
          cancellationToken);
      await File.WriteAllTextAsync(
          snapshotPath,
          snapshotJson.Replace(
              "\"state\":\"starting\"",
              "\"state\":\"unsafe\"",
              StringComparison.Ordinal),
          cancellationToken);
      var invalid = await replayStore.ReadPendingAsync(
          cancellationToken);

      await Assert.That(invalid.Replay).IsNull();
      await Assert.That(invalid.HasPendingEvents).IsFalse();
      await Assert.That(invalid.RequiresSynchronization).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  private static ConnectorHealthJournal CreateJournal(
      string root)
  {
    var options = ConnectorTestData.CreateOptions(
        Path.Combine(root, "state"),
        Path.Combine(root, "identity.json"));
    return new ConnectorHealthJournal(
        Options.Create(options),
        TimeProvider.System,
        NullLogger<ConnectorHealthJournal>.Instance);
  }

  private static ConnectorHealthReplayStore CreateReplayStore(
      string root)
  {
    var options = ConnectorTestData.CreateOptions(
        Path.Combine(root, "state"),
        Path.Combine(root, "identity.json"));
    return new ConnectorHealthReplayStore(
        Options.Create(options),
        TimeProvider.System,
        NullLogger<ConnectorHealthReplayStore>.Instance);
  }

  private static string CreateTemporaryDirectory()
  {
    var path = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-connector-health-replay-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
  }
}
