using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ConnectorHealthJournalTests
{
  [Test]
  public async Task Failure_And_Recovery_Are_Durable_And_Redacted(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var journal = CreateJournal(root);
      var startedAt = new DateTimeOffset(
          2026,
          8,
          7,
          1,
          0,
          0,
          TimeSpan.Zero);
      await journal.RecordProcessStartedAsync(
          startedAt,
          cancellationToken);
      await journal.RecordFailureAsync(
          ConnectorHealthEventKinds.ObservationIncomplete,
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.ProfileStateInvalid,
              "https://user:password@example.test/?token=secret C:\\Users\\Nick\\state",
              "default"),
          3,
          TimeSpan.FromMinutes(5),
          startedAt.AddMinutes(1),
          cancellationToken);
      await journal.RecordSynchronizationSucceededAsync(
          startedAt.AddMinutes(6),
          cancellationToken);
      await journal.RecordSynchronizationSucceededAsync(
          startedAt.AddMinutes(6),
          cancellationToken);

      var snapshot = await ReadSnapshotAsync(
          root,
          cancellationToken);
      var events = await ReadEventsAsync(
          root,
          cancellationToken);
      var serialized = await File.ReadAllTextAsync(
          GetSnapshotPath(root),
          cancellationToken) +
          await File.ReadAllTextAsync(
              GetEventsPath(root),
              cancellationToken);

      await Assert.That(snapshot.State)
          .IsEqualTo(ConnectorHealthStates.Healthy);
      await Assert.That(snapshot.LastSuccessAt)
          .IsEqualTo(
              new DateTimeOffset(
                  2026,
                  8,
                  7,
                  1,
                  6,
                  0,
                  TimeSpan.Zero));
      await Assert.That(snapshot.ActiveOutageId).IsNull();
      await Assert.That(snapshot.LastRecoveredOutageId).IsNotNull();
      await Assert.That(snapshot.LastRecoveredFailureCategory)
          .IsEqualTo(
              ConnectorHealthFailureCategories.ProfileStateInvalid);
      await Assert.That(snapshot.LastFailureProfileId)
          .IsEqualTo("default");
      await Assert.That(snapshot.LastFailureDetail)
          .IsEqualTo("Profile observed state is invalid.");
      await Assert.That(events).Count().IsEqualTo(4);
      await Assert.That(events[0].Kind)
          .IsEqualTo(ConnectorHealthEventKinds.ProcessStarted);
      await Assert.That(events[1].Kind)
          .IsEqualTo(ConnectorHealthEventKinds.ObservationIncomplete);
      await Assert.That(events[2].Kind)
          .IsEqualTo(ConnectorHealthEventKinds.Recovered);
      await Assert.That(events[3].Kind)
          .IsEqualTo(
              ConnectorHealthEventKinds.SynchronizationSucceeded);
      await Assert.That(events[3].FailureCategory).IsNull();
      await Assert.That(serialized).DoesNotContain("password");
      await Assert.That(serialized).DoesNotContain("token=secret");
      await Assert.That(serialized).DoesNotContain("C:\\Users");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Restart_Preserves_An_Active_Outage(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var startedAt = new DateTimeOffset(
          2026,
          8,
          7,
          2,
          0,
          0,
          TimeSpan.Zero);
      var first = CreateJournal(root);
      await first.RecordProcessStartedAsync(
          startedAt,
          cancellationToken);
      await first.RecordFailureAsync(
          ConnectorHealthEventKinds.SynchronizationFailed,
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.SynchronizationNetwork,
              "ignored"),
          4,
          TimeSpan.FromMinutes(5),
          startedAt.AddMinutes(1),
          cancellationToken);
      var beforeRestart = await ReadSnapshotAsync(
          root,
          cancellationToken);

      var second = CreateJournal(root);
      await second.RecordProcessStartedAsync(
          startedAt.AddMinutes(3),
          cancellationToken);
      var afterRestart = await ReadSnapshotAsync(
          root,
          cancellationToken);

      await Assert.That(afterRestart.State)
          .IsEqualTo(ConnectorHealthStates.Degraded);
      await Assert.That(afterRestart.ActiveOutageId)
          .IsEqualTo(beforeRestart.ActiveOutageId);
      await Assert.That(afterRestart.ActiveOutageStartedAt)
          .IsEqualTo(beforeRestart.ActiveOutageStartedAt);
      await Assert.That(afterRestart.ProcessStartedAt)
          .IsEqualTo(startedAt.AddMinutes(3));
      await Assert.That(afterRestart.ConsecutiveFailures)
          .IsEqualTo(4);
      var events = await ReadEventsAsync(
          root,
          cancellationToken);
      await Assert.That(events[^1].FailureCategory)
          .IsEqualTo(
              ConnectorHealthFailureCategories.SynchronizationNetwork);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  [Arguments(
      ConnectorHealthFailureCategories.PayloadRejected,
      "Dashboard permanently rejected the synchronization payload.")]
  [Arguments(
      ConnectorHealthFailureCategories.CredentialRejected,
      "Dashboard rejected the connector credential.")]
  [Arguments(
      ConnectorHealthFailureCategories.EnrollmentRejected,
      "Dashboard rejected connector enrollment.")]
  public async Task Rejection_Failures_Are_Sanitized(
      string category,
      string expectedDetail,
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var journal = CreateJournal(root);
      await journal.RecordFailureAsync(
          ConnectorHealthEventKinds.Rejected,
          new ConnectorHealthFailure(
              category,
              "secret rejection payload https://example.test/?token=secret",
              "..\\secret-profile"),
          1,
          null,
          DateTimeOffset.UtcNow,
          cancellationToken);

      var snapshot = await ReadSnapshotAsync(
          root,
          cancellationToken);
      var serialized = await File.ReadAllTextAsync(
          GetSnapshotPath(root),
          cancellationToken) +
          await File.ReadAllTextAsync(
              GetEventsPath(root),
              cancellationToken);
      await Assert.That(snapshot.LastFailureCategory)
          .IsEqualTo(category);
      await Assert.That(snapshot.LastFailureDetail)
          .IsEqualTo(expectedDetail);
      await Assert.That(snapshot.LastFailureProfileId).IsNull();
      await Assert.That(serialized).DoesNotContain("secret");
      await Assert.That(serialized).DoesNotContain("example.test");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Event_Journal_Retains_Only_The_Newest_256_Entries(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var startedAt = new DateTimeOffset(
          2026,
          8,
          7,
          3,
          0,
          0,
          TimeSpan.Zero);
      var journal = CreateJournal(root);
      await journal.RecordProcessStartedAsync(
          startedAt,
          cancellationToken);
      for (var index = 0; index < 260; index++)
      {
        await journal.RecordFailureAsync(
            ConnectorHealthEventKinds.SynchronizationFailed,
            new ConnectorHealthFailure(
                ConnectorHealthFailureCategories.SynchronizationNetwork,
                "ignored"),
            index + 1,
            TimeSpan.FromSeconds(30),
            startedAt.AddSeconds(index + 1),
            cancellationToken);
      }

      var events = await ReadEventsAsync(
          root,
          cancellationToken);
      var temporaryFiles = Directory.GetFiles(
          GetHealthDirectory(root),
          "*.tmp");

      await Assert.That(events).Count().IsEqualTo(256);
      await Assert.That(events[0].OccurredAt)
          .IsEqualTo(startedAt.AddSeconds(5));
      await Assert.That(events[^1].OccurredAt)
          .IsEqualTo(startedAt.AddSeconds(260));
      await Assert.That(events.All(entry =>
              entry.Kind ==
                  ConnectorHealthEventKinds.SynchronizationFailed))
          .IsTrue()
          .Because("only the newest synchronization failures should remain");
      await Assert.That(temporaryFiles).IsEmpty();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Process_Stopping_Is_Recorded_Without_Losing_Recovery(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var startedAt = new DateTimeOffset(
          2026,
          8,
          7,
          4,
          0,
          0,
          TimeSpan.Zero);
      var journal = CreateJournal(root);
      await journal.RecordProcessStartedAsync(
          startedAt,
          cancellationToken);
      await journal.RecordSynchronizationSucceededAsync(
          startedAt.AddSeconds(10),
          cancellationToken);
      await journal.RecordProcessStoppingAsync(
          startedAt.AddSeconds(20),
          cancellationToken);

      var snapshot = await ReadSnapshotAsync(
          root,
          cancellationToken);
      var events = await ReadEventsAsync(
          root,
          cancellationToken);

      await Assert.That(snapshot.State)
          .IsEqualTo(ConnectorHealthStates.Stopping);
      await Assert.That(snapshot.LastSuccessAt)
          .IsEqualTo(startedAt.AddSeconds(10));
      await Assert.That(events[^1].Kind)
          .IsEqualTo(ConnectorHealthEventKinds.ProcessStopping);
      await Assert.That(events[^1].FailureCategory).IsNull();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Malformed_Snapshot_Is_Replaced(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      Directory.CreateDirectory(GetHealthDirectory(root));
      await File.WriteAllTextAsync(
          GetSnapshotPath(root),
          "{",
          cancellationToken);
      var journal = CreateJournal(root);
      var startedAt = new DateTimeOffset(
          2026,
          8,
          7,
          5,
          0,
          0,
          TimeSpan.Zero);

      await journal.RecordProcessStartedAsync(
          startedAt,
          cancellationToken);

      var snapshot = await ReadSnapshotAsync(
          root,
          cancellationToken);
      await Assert.That(snapshot.State)
          .IsEqualTo(ConnectorHealthStates.Starting);
      await Assert.That(snapshot.ProcessStartedAt)
          .IsEqualTo(startedAt);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Snapshot_Persists_When_Event_Journal_Cannot_Be_Written(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      Directory.CreateDirectory(GetHealthDirectory(root));
      Directory.CreateDirectory(GetEventsPath(root));
      var journal = CreateJournal(root);
      var startedAt = new DateTimeOffset(
          2026,
          8,
          7,
          6,
          0,
          0,
          TimeSpan.Zero);

      await journal.RecordFailureAsync(
          ConnectorHealthEventKinds.SynchronizationFailed,
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.SynchronizationNetwork,
              "ignored"),
          1,
          TimeSpan.FromSeconds(30),
          startedAt,
          cancellationToken);

      var snapshot = await ReadSnapshotAsync(
          root,
          cancellationToken);
      await Assert.That(snapshot.State)
          .IsEqualTo(ConnectorHealthStates.Degraded);
      await Assert.That(snapshot.LastFailureCategory)
          .IsEqualTo(
              ConnectorHealthFailureCategories.SynchronizationNetwork);
      await Assert.That(snapshot.NextRetryAt)
          .IsEqualTo(startedAt.AddSeconds(30));
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Oversized_Event_Journal_Is_Replaced(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      Directory.CreateDirectory(GetHealthDirectory(root));
      await File.WriteAllTextAsync(
          GetEventsPath(root),
          new string('x', 1_100_000),
          cancellationToken);
      var journal = CreateJournal(root);
      var startedAt = new DateTimeOffset(
          2026,
          8,
          7,
          7,
          0,
          0,
          TimeSpan.Zero);

      await journal.RecordProcessStartedAsync(
          startedAt,
          cancellationToken);

      var events = await ReadEventsAsync(
          root,
          cancellationToken);
      await Assert.That(events).HasSingleItem();
      await Assert.That(events[0].Kind)
          .IsEqualTo(ConnectorHealthEventKinds.ProcessStarted);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Overlong_Event_Line_Is_Discarded(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      Directory.CreateDirectory(GetHealthDirectory(root));
      await File.WriteAllTextAsync(
          GetEventsPath(root),
          new string('x', 5_000) + "\n",
          cancellationToken);
      var journal = CreateJournal(root);
      var startedAt = new DateTimeOffset(
          2026,
          8,
          7,
          7,
          30,
          0,
          TimeSpan.Zero);

      await journal.RecordProcessStartedAsync(
          startedAt,
          cancellationToken);

      var events = await ReadEventsAsync(
          root,
          cancellationToken);
      await Assert.That(events).HasSingleItem();
      await Assert.That(events[0].Kind)
          .IsEqualTo(ConnectorHealthEventKinds.ProcessStarted);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Restart_Uses_The_Active_Outage_Category(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var journal = CreateJournal(root);
      var startedAt = new DateTimeOffset(
          2026,
          8,
          7,
          8,
          0,
          0,
          TimeSpan.Zero);
      await journal.RecordFailureAsync(
          ConnectorHealthEventKinds.SynchronizationFailed,
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.SynchronizationNetwork,
              "ignored"),
          1,
          TimeSpan.FromSeconds(30),
          startedAt,
          cancellationToken);
      await journal.RecordSynchronizationSucceededAsync(
          startedAt.AddMinutes(1),
          cancellationToken);
      await journal.RecordFailureAsync(
          ConnectorHealthEventKinds.SynchronizationFailed,
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.SynchronizationServer,
              "ignored"),
          1,
          TimeSpan.FromSeconds(30),
          startedAt.AddMinutes(2),
          cancellationToken);
      await journal.RecordProcessStartedAsync(
          startedAt.AddMinutes(3),
          cancellationToken);

      var events = await ReadEventsAsync(
          root,
          cancellationToken);
      await Assert.That(events[^1].Kind)
          .IsEqualTo(ConnectorHealthEventKinds.ProcessStarted);
      await Assert.That(events[^1].FailureCategory)
          .IsEqualTo(
              ConnectorHealthFailureCategories.SynchronizationServer);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task Timed_Out_Update_Preserves_Terminal_Transition_Order(
      CancellationToken cancellationToken)
  {
    var root = CreateTemporaryDirectory();
    try
    {
      var journal = CreateJournal(root);
      var release = new CancellationTokenSource();
      var started = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var failureAt = new DateTimeOffset(
          2026,
          8,
          7,
          9,
          0,
          0,
          TimeSpan.Zero);

      var firstUpdate = journal.RecordBestEffortAsync(
          async _ =>
          {
            started.SetResult();
            try
            {
              await Task.Delay(
                  Timeout.InfiniteTimeSpan,
                  release.Token);
            }
            catch (OperationCanceledException)
                when (release.IsCancellationRequested)
            {
            }
          },
          cancellationToken);
      await started.Task.WaitAsync(cancellationToken);
      await firstUpdate;
      var failureUpdate = journal.RecordFailureAsync(
          ConnectorHealthEventKinds.SynchronizationFailed,
          new ConnectorHealthFailure(
              ConnectorHealthFailureCategories.SynchronizationNetwork,
              "ignored"),
          2,
          TimeSpan.FromSeconds(30),
          failureAt,
          cancellationToken);
      await journal.RecordSynchronizationAttemptAsync(
          failureAt.AddSeconds(1),
          cancellationToken);
      var successUpdate = journal.RecordSynchronizationSucceededAsync(
          failureAt.AddMinutes(1),
          cancellationToken);
      var stoppingUpdate = journal.RecordProcessStoppingAsync(
          failureAt.AddMinutes(2),
          cancellationToken);

      await release.CancelAsync();
      await Task.WhenAll(
          failureUpdate,
          successUpdate,
          stoppingUpdate).WaitAsync(
              TimeSpan.FromSeconds(5),
              cancellationToken);

      var snapshot = await ReadSnapshotAsync(
          root,
          cancellationToken);
      await Assert.That(snapshot.State)
          .IsEqualTo(ConnectorHealthStates.Stopping);
      await Assert.That(snapshot.ActiveOutageId).IsNull();
      await Assert.That(snapshot.LastRecoveredFailureCategory)
          .IsEqualTo(
              ConnectorHealthFailureCategories.SynchronizationNetwork);
      await Assert.That(snapshot.LastFailureAt)
          .IsEqualTo(failureAt);
      await Assert.That(snapshot.LastRecoveredAt)
          .IsEqualTo(failureAt.AddMinutes(1));
      var events = await ReadEventsAsync(
          root,
          cancellationToken);
      await Assert.That(events).Count().IsEqualTo(3);
      await Assert.That(events[0].Kind)
          .IsEqualTo(
              ConnectorHealthEventKinds.SynchronizationFailed);
      await Assert.That(events[1].Kind)
          .IsEqualTo(ConnectorHealthEventKinds.Recovered);
      await Assert.That(events[2].Kind)
          .IsEqualTo(ConnectorHealthEventKinds.ProcessStopping);
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

  private static async Task<ConnectorHealthSnapshot> ReadSnapshotAsync(
      string root,
      CancellationToken cancellationToken)
  {
    await using var stream = File.OpenRead(
        GetSnapshotPath(root));
    return await JsonSerializer.DeserializeAsync(
        stream,
        ConnectorHealthJsonContext.Default.ConnectorHealthSnapshot,
        cancellationToken) ??
        throw new InvalidOperationException(
            "Connector health snapshot was empty.");
  }

  private static async Task<IReadOnlyList<ConnectorHealthEvent>> ReadEventsAsync(
      string root,
      CancellationToken cancellationToken)
  {
    var events = new List<ConnectorHealthEvent>();
    foreach (var line in await File.ReadAllLinesAsync(
        GetEventsPath(root),
        cancellationToken))
    {
      events.Add(
          JsonSerializer.Deserialize(
              line,
              ConnectorHealthJsonContext.Default.ConnectorHealthEvent) ??
          throw new InvalidOperationException(
              "Connector health event was empty."));
    }
    return events;
  }

  private static string GetHealthDirectory(string root) =>
      Path.Combine(root, "health");

  private static string GetSnapshotPath(string root) =>
      Path.Combine(
          GetHealthDirectory(root),
          "connector-health.json");

  private static string GetEventsPath(string root) =>
      Path.Combine(
          GetHealthDirectory(root),
          "connector-events.jsonl");

  private static string CreateTemporaryDirectory()
  {
    var path = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-connector-health-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
  }
}
