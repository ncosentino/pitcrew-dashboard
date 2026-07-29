using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteFleetHistoryStoreTests
{
  private static readonly DateTimeOffset Origin = new(
      2026,
      7,
      24,
      12,
      0,
      0,
      TimeSpan.Zero);

  private const int NodePointLimit = 100_000;

  private const int NodeEventLimit = 100_000;

  private const int NodeDiagnosticLimit = 100_000;

  private static readonly HistoryRetentionPolicy Retention = CreateRetention(
      TimeSpan.FromDays(14),
      TimeSpan.FromDays(90),
      TimeSpan.FromDays(30),
      100_000,
      20_000,
      500_000,
      200_000,
      200_000);

  private static readonly HistoryAppendPolicy AppendPolicy = new(
      Retention,
      TimeSpan.FromMinutes(5));

  [Test]
  public async Task Duplicate_Heartbeat_Creates_No_Duplicate_Sample_Or_Event(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("duplicate");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var profile = CreateProfile(
          Origin,
          journal: CreateJournal(
              "current",
              [CreateEvent(1, Origin), CreateEvent(2, Origin)]));

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [profile],
          Origin,
          Retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [profile],
          Origin.AddSeconds(15),
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Samples).HasSingleItem();
      await Assert.That(history.Samples[0].ObservedAt)
          .IsEqualTo(Origin);
      await Assert.That(history.Events.Count).IsEqualTo(2);
      await Assert.That(history.Journal.MissedEvents).IsEqualTo(0L);
      await Assert.That(history.Journal.UndeliveredEvents).IsEqualTo(0L);
      await Assert.That(history.Journal.StoredHighestSequence)
          .IsEqualTo(2L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Worker_Image_Rollout_Transitions_Are_Durable_And_Bounded(
        CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("worker-update-history");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var imageOne = "ghcr.io/example/runner@sha256:" + new string('1', 64);
      var imageTwo = "ghcr.io/example/runner@sha256:" + new string('2', 64);
      var imageIdOne = "sha256:" + new string('1', 64);
      var imageIdTwo = "sha256:" + new string('2', 64);
      var revisionOne = new string('a', 64);
      var revisionTwo = new string('b', 64);
      var observations = new[]
      {
          CreateProfile(Origin) with
          {
            Update = new ManagerWorkerUpdateState(
                "current",
                imageOne,
                imageIdOne,
                revisionOne,
                2,
                0,
                null),
          },
          CreateProfile(Origin.AddMinutes(1)) with
          {
            Update = new ManagerWorkerUpdateState(
                "rolling",
                imageTwo,
                imageIdTwo,
                revisionTwo,
                1,
                1,
                null),
          },
          CreateProfile(Origin.AddMinutes(2)) with
          {
            Update = new ManagerWorkerUpdateState(
                "degraded",
                imageTwo,
                imageIdTwo,
                revisionTwo,
                1,
                1,
                "candidate verification delayed"),
          },
          CreateProfile(Origin.AddMinutes(3)) with
          {
            Update = new ManagerWorkerUpdateState(
                "current",
                imageTwo,
                imageIdTwo,
                revisionTwo,
                2,
                0,
                null),
          },
      };
      foreach (var observation in observations)
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [observation],
            observation.ObservedAt,
            Retention,
            cancellationToken);
      }

      var restartedStore = new SqliteFleetHistoryStore(connectionFactory);
      var history = await ReadProfileHistoryAsync(
          restartedStore,
          nodeId,
          cancellationToken);
      var kinds = history.WorkerUpdateChanges
          .Select(change => change.Kind)
          .ToArray();

      await Assert.That(history.Samples).Count().IsEqualTo(4);
      await Assert.That(history.Samples[1].WorkerUpdateStatus)
          .IsEqualTo("rolling");
      await Assert.That(kinds.Contains("target-changed")).IsTrue();
      await Assert.That(kinds.Contains("rollout-started")).IsTrue();
      await Assert.That(kinds.Contains("rollout-degraded")).IsTrue();
      await Assert.That(kinds.Contains("rollout-converged")).IsTrue();
      await Assert.That(history.WorkerUpdatesTruncated).IsFalse();

      var bounded = await restartedStore.GetProfileHistoryAsync(
          "tenant",
          nodeId,
          "default",
          new HistoryWindow(
              Origin.AddDays(-1),
              Origin.AddDays(1),
              HistoryResolution.Raw,
              100,
              100,
              2,
              NodePointLimit,
              NodeEventLimit,
              NodeDiagnosticLimit),
          Origin.AddMinutes(4),
          cancellationToken);
      await Assert.That(bounded).IsNotNull();
      await Assert.That(bounded!.Profiles).HasSingleItem();
      await Assert.That(bounded.Profiles[0].WorkerUpdateChanges)
          .Count()
          .IsEqualTo(2);
      await Assert.That(bounded.Profiles[0].WorkerUpdatesTruncated).IsTrue();
      await Assert.That(bounded.ProfileWorkerUpdateLimit).IsEqualTo(2);
      await Assert.That(bounded.DiagnosticsTruncated).IsTrue();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Equal_Observation_Appends_New_Journal_Events_Without_Duplicating_The_Sample(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("equal-observation-events");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, Origin)])),
          ],
          Origin,
          Retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, Origin), CreateEvent(2, Origin)])),
          ],
          Origin.AddSeconds(15),
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Samples).HasSingleItem();
      await Assert.That(history.Events.Count).IsEqualTo(2);
      await Assert.That(history.Journal.StoredHighestSequence).IsEqualTo(2L);
      await Assert.That(history.Journal.EpochResets).IsEqualTo(0L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Advancing_Observations_Append_Samples_And_Rollups(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("advance");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      for (var index = 0; index < 4; index++)
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateProfile(
                    Origin.AddMinutes(index),
                    activeSlots: index,
                    managerCpuCores: 0.5 + index),
            ],
            Origin.AddMinutes(index),
            Retention,
            cancellationToken);
      }

      var raw = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(raw.Samples.Count).IsEqualTo(4);
      await Assert.That(raw.Samples[0].ObservedAt).IsEqualTo(Origin);
      await Assert.That(raw.Samples[3].ActiveSlots).IsEqualTo(3);
      await Assert.That(raw.Samples[3].ManagerCpuCores).IsEqualTo(3.5);
      await Assert.That(raw.Samples[0].WorkerCpuCores).IsEqualTo(1.5);
      await Assert.That(raw.Samples[0].NetworkRxBytes).IsEqualTo(2048L);
      await Assert.That(raw.Samples[0].ExitReports).IsEqualTo(1);
      await Assert.That(raw.Samples[0].AdverseExitReports).IsEqualTo(1);
      await Assert.That(raw.Samples[0].LocalCapacityDeficit).IsEqualTo(2);
      await Assert.That(raw.Samples[0].CapacityDeficitReason)
          .IsEqualTo("image-pull-backoff");

      var hourly = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Hourly);
      await Assert.That(hourly.Rollups).HasSingleItem();
      await Assert.That(hourly.Rollups[0].SampleCount).IsEqualTo(4);
      await Assert.That(hourly.Rollups[0].MaximumActiveSlots).IsEqualTo(3);
      await Assert.That(hourly.Rollups[0].MaximumManagerCpuCores)
          .IsEqualTo(3.5);
      await Assert.That(hourly.Rollups[0].BucketStart).IsEqualTo(Origin);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Journal_Gaps_And_Undelivered_Events_Remain_Explicit(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("gaps");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, Origin), CreateEvent(2, Origin)])),
          ],
          Origin,
          Retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin.AddMinutes(1),
                  journal: CreateJournal(
                      "truncated",
                      [CreateEvent(9, Origin.AddMinutes(1))],
                      highestSequence: 12,
                      droppedEvents: 6)),
          ],
          Origin.AddMinutes(1),
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Journal.Status).IsEqualTo("truncated");
      await Assert.That(history.Journal.MissedEvents).IsEqualTo(6L);
      await Assert.That(history.Journal.UndeliveredEvents).IsEqualTo(3L);
      await Assert.That(history.Journal.ManagerDroppedEvents).IsEqualTo(6);
      await Assert.That(history.Journal.StoredLowestSequence).IsEqualTo(1L);
      await Assert.That(history.Journal.StoredHighestSequence).IsEqualTo(9L);
      await Assert.That(history.Events.Count).IsEqualTo(3);
      await Assert.That(history.Events[0].Sequence).IsEqualTo(9L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task History_Queries_Are_Isolated_By_Tenant(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("tenant");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin)],
          Origin,
          Retention,
          cancellationToken);

      var foreignNode = await store.GetNodeHistoryAsync(
          "other-tenant",
          nodeId,
          CreateWindow(HistoryResolution.Raw, 100, 100),
          Origin.AddMinutes(1),
          cancellationToken);
      var foreignProfile = await store.GetProfileHistoryAsync(
          "other-tenant",
          nodeId,
          "default",
          CreateWindow(HistoryResolution.Raw, 100, 100),
          Origin.AddMinutes(1),
          cancellationToken);
      await Assert.That(foreignNode).IsNull();
      await Assert.That(foreignProfile).IsNull();

      var owned = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          CreateWindow(HistoryResolution.Raw, 100, 100),
          Origin.AddMinutes(1),
          cancellationToken);
      await Assert.That(owned).IsNotNull();
      await Assert.That(owned!.Profiles).HasSingleItem();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Retention_Bounds_Samples_Rollups_And_Events(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("retention");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = CreateRetention(
          TimeSpan.FromMinutes(30),
          TimeSpan.FromMinutes(90),
          TimeSpan.FromMinutes(30),
          2,
          2,
          500_000,
          200_000,
          200_000);
      for (var index = 0; index < 5; index++)
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateProfile(
                    Origin.AddMinutes(index),
                    journal: CreateJournal(
                        "current",
                        [CreateEvent(index + 1, Origin.AddMinutes(index))])),
            ],
            Origin.AddMinutes(index),
            retention,
            cancellationToken);
      }

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Samples.Count).IsEqualTo(2);
      await Assert.That(history.Samples[0].ObservedAt)
          .IsEqualTo(Origin.AddMinutes(3));
      await Assert.That(history.Events.Count).IsEqualTo(2);
      await Assert.That(history.Events[0].Sequence).IsEqualTo(5L);

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin.AddHours(3))],
          Origin.AddHours(3),
          retention,
          cancellationToken);
      var aged = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Hourly,
          Origin.AddHours(4));
      await Assert.That(aged.Rollups).HasSingleItem();
      await Assert.That(aged.Rollups[0].BucketStart)
          .IsEqualTo(Origin.AddHours(3));
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Point_And_Event_Limits_Report_Truncation(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("bounds");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      for (var index = 0; index < 6; index++)
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateProfile(
                    Origin.AddMinutes(index),
                    journal: CreateJournal(
                        "current",
                        [CreateEvent(index + 1, Origin.AddMinutes(index))])),
            ],
            Origin.AddMinutes(index),
            Retention,
            cancellationToken);
      }

      var bounded = await store.GetProfileHistoryAsync(
          "tenant",
          nodeId,
          "default",
          CreateWindow(HistoryResolution.Raw, 2, 3),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(bounded).IsNotNull();
      var history = bounded!.Profiles[0];
      await Assert.That(history.Samples.Count).IsEqualTo(2);
      await Assert.That(history.PointsTruncated).IsTrue();
      await Assert.That(history.Samples[1].ObservedAt)
          .IsEqualTo(Origin.AddMinutes(5));
      await Assert.That(history.Events.Count).IsEqualTo(3);
      await Assert.That(history.EventsTruncated).IsTrue();
      await Assert.That(history.Events[0].Sequence).IsEqualTo(6L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Representative_Growth_Stays_Within_Measured_Budget(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("growth");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      const int samples = 960;
      await CheckpointAsync(connectionFactory, cancellationToken);
      var baseline = new FileInfo(databasePath).Length;
      long peakWalBytes = 0;
      for (var index = 0; index < samples; index++)
      {
        var observedAt = Origin.AddSeconds(15 * index);
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateProfile(
                    observedAt,
                    activeSlots: index % 5,
                    managerCpuCores: 0.25 + (index % 7),
                    journal: index % 20 == 0
                        ? CreateJournal(
                            "current",
                            [
                                CreateEvent(
                                    (index / 20) + 1,
                                    observedAt),
                            ])
                        : null),
            ],
            observedAt,
            Retention,
            cancellationToken);
        var wal = new FileInfo(databasePath + "-wal");
        if (wal.Exists && wal.Length > peakWalBytes)
        {
          peakWalBytes = wal.Length;
        }
      }

      await CheckpointAsync(connectionFactory, cancellationToken);
      var checkpointed = new FileInfo(databasePath).Length;
      var growth = checkpointed - baseline;
      var bytesPerSample = growth / (double)samples;
      var measurement = string.Create(
          CultureInfo.InvariantCulture,
          $"Measured checkpointed history growth: {growth} bytes for {samples} samples ({bytesPerSample:F1} bytes per sample, including hourly rollups, manager events, subsystem health, capacity deficit and cursor overhead). Peak write-ahead log: {peakWalBytes} bytes.");
      if (TestContext.Current is { } testContext)
      {
        await testContext.OutputWriter.WriteLineAsync(measurement);
      }
      await Assert.That(bytesPerSample).IsLessThan(550d);
      await Assert.That(peakWalBytes).IsLessThan(8L * 1024 * 1024);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Raw,
          Origin.AddDays(1),
          1000);
      await Assert.That(history.Samples.Count).IsEqualTo(samples);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Migration_Seven_Backup_Restores_History_Consistently(
      CancellationToken cancellationToken)
  {
    var sourcePath = CreateDatabasePath("backup-source");
    var destinationPath = CreateDatabasePath("backup-destination");
    var backupPath = sourcePath + ".backup";
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          sourcePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, Origin)])),
          ],
          Origin,
          Retention,
          cancellationToken);
      var (destinationFactory, _) = await CreateEnrolledNodeAsync(
          destinationPath,
          cancellationToken);
      SqliteConnection.ClearAllPools();
      var maintenance = new SqliteDatabaseMaintenance();

      var backup = maintenance.Backup(
          sourcePath,
          backupPath,
          cancellationToken);
      var verification = maintenance.Verify(backupPath, cancellationToken);
      var restore = maintenance.Restore(
          backupPath,
          destinationPath,
          cancellationToken);

      await Assert.That(backup.Succeeded).IsTrue();
      await Assert.That(verification.Succeeded).IsTrue();
      await Assert.That(restore.Succeeded).IsTrue();
      var restored = new SqliteFleetHistoryStore(destinationFactory);
      var history = await restored.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          CreateWindow(HistoryResolution.Raw, 100, 100),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var profile = history!.Profiles[0];
      await Assert.That(profile.Samples.Count).IsEqualTo(1);
      await Assert.That(profile.Events.Count).IsEqualTo(1);
      await Assert.That(profile.Journal.StoredHighestSequence).IsEqualTo(1L);
      var hourly = await restored.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          CreateWindow(HistoryResolution.Hourly, 100, 100),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(hourly!.Profiles[0].Rollups.Count).IsEqualTo(1);
    }
    finally
    {
      Cleanup(sourcePath);
      Cleanup(destinationPath);
      if (File.Exists(backupPath))
      {
        File.Delete(backupPath);
      }
    }
  }

  private static async Task CheckpointAsync(
      SqliteConnectionFactory connectionFactory,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  [Test]
  public async Task Uncommitted_Transaction_Persists_No_History(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("atomic");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await using (var transaction = await SqliteFleetTransaction.BeginAsync(
          connectionFactory,
          cancellationToken))
      {
        await store.AppendAsync(
            transaction,
            nodeId,
            [
                CreateProfile(
                    Origin,
                    journal: CreateJournal(
                        "current",
                        [CreateEvent(1, Origin)])),
            ],
            Origin,
            AppendPolicy,
            cancellationToken);
      }

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          CreateWindow(HistoryResolution.Raw, 100, 100),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      await Assert.That(history!.Profiles.Count).IsEqualTo(0);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Journal_Sequence_Regression_Starts_New_Epoch(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("epoch");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, Origin), CreateEvent(2, Origin)])),
          ],
          Origin,
          Retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin.AddMinutes(1),
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, Origin.AddMinutes(1))])),
          ],
          Origin.AddMinutes(1),
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Journal.Epoch).IsEqualTo(1L);
      await Assert.That(history.Journal.EpochResets).IsEqualTo(1L);
      await Assert.That(history.Journal.StoredHighestSequence).IsEqualTo(1L);
      await Assert.That(history.Events.Count).IsEqualTo(3);
      await Assert.That(history.Journal.MissedEvents).IsEqualTo(0L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Restarted_Manager_Replay_Does_Not_Start_New_Epoch(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("replay");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var journal = CreateJournal(
          "current",
          [CreateEvent(1, Origin), CreateEvent(2, Origin)]);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin, journal: journal)],
          Origin,
          Retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin.AddMinutes(1), journal: journal)],
          Origin.AddMinutes(1),
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Journal.Epoch).IsEqualTo(0L);
      await Assert.That(history.Journal.EpochResets).IsEqualTo(0L);
      await Assert.That(history.Events.Count).IsEqualTo(2);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Implausibly_Future_Observations_Are_Rejected_And_Counted(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("skew");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var future = Origin.AddHours(6);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  future,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, future)])),
          ],
          Origin,
          AppendPolicy,
          cancellationToken);

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          new HistoryWindow(
              Origin.AddDays(-1),
              Origin.AddDays(7),
              HistoryResolution.Raw,
              100,
              100,
              NodeDiagnosticLimit,
              NodePointLimit,
              NodeEventLimit,
              NodeDiagnosticLimit),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var profile = history!.Profiles[0];
      await Assert.That(profile.Samples.Count).IsEqualTo(0);
      await Assert.That(profile.Events.Count).IsEqualTo(0);
      await Assert.That(profile.Retention.RejectedFutureSamples)
          .IsEqualTo(1L);
      await Assert.That(profile.Journal.RejectedFutureEvents).IsEqualTo(1L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Subsystem_Health_And_Every_Target_Deficit_Are_Persisted(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("contract12");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateAutoscalingProfile(Origin, "degraded", 3)],
          Origin,
          Retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateAutoscalingProfile(Origin.AddMinutes(1), "degraded", 3)],
          Origin.AddMinutes(1),
          Retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateAutoscalingProfile(Origin.AddMinutes(2), "healthy", 0)],
          Origin.AddMinutes(2),
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.SubsystemHealthChanges.Count).IsEqualTo(3);
      await Assert.That(
          history.SubsystemHealthChanges
              .Count(change => change.Subsystem == "docker"))
          .IsEqualTo(2);
      await Assert.That(
          history.CapacityDeficits
              .Select(deficit => deficit.TargetKey)
              .Distinct()
              .Count())
          .IsEqualTo(2);
      await Assert.That(
          history.CapacityDeficits.Count(
              deficit => deficit.TargetKey == "owner/repo-a"))
          .IsEqualTo(2);
      await Assert.That(
          history.CapacityDeficits.Any(
              deficit => deficit.Reason == "scale-set-throttled"))
          .IsTrue();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Retention_Sweeps_Profiles_Absent_From_The_Newest_Heartbeat(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("churn");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = CreateRetention(
          TimeSpan.FromMinutes(30),
          TimeSpan.FromDays(90),
          TimeSpan.FromMinutes(30),
          100,
          100,
          100_000,
          100_000,
          100_000);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin) with { ProfileId = "removed" }],
          Origin,
          retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin.AddHours(2))],
          Origin.AddHours(2),
          retention,
          cancellationToken);

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          new HistoryWindow(
              Origin.AddDays(-1),
              Origin.AddDays(1),
              HistoryResolution.Raw,
              100,
              100,
              NodeDiagnosticLimit,
              NodePointLimit,
              NodeEventLimit,
              NodeDiagnosticLimit),
          Origin.AddHours(3),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var removed = history!.Profiles.Single(
          profile => profile.ProfileId == "removed");
      await Assert.That(removed.Samples.Count).IsEqualTo(0);
      await Assert.That(removed.Retention.DroppedSamples).IsEqualTo(1L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Node_Wide_Sample_Cap_Bounds_Profile_Identifier_Churn(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("nodecap");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = CreateRetention(
          TimeSpan.FromDays(7),
          TimeSpan.FromDays(90),
          TimeSpan.FromDays(30),
          100,
          100,
          3,
          100,
          100);
      for (var index = 0; index < 5; index++)
      {
        var observedAt = Origin.AddMinutes(index);
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateProfile(observedAt) with
                {
                  ProfileId = string.Create(
                      CultureInfo.InvariantCulture,
                      $"profile-{index}"),
                },
            ],
            observedAt,
            retention,
            cancellationToken);
      }

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          CreateWindow(HistoryResolution.Raw, 100, 100),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var retained = history!.Profiles.Sum(
          profile => profile.Samples.Count);
      await Assert.That(retained).IsLessThanOrEqualTo(3);
      await Assert.That(retained).IsGreaterThan(0);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Node_Point_Limit_Truncates_The_Whole_Node_Response(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("nodelimit");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      for (var index = 0; index < 3; index++)
      {
        var observedAt = Origin.AddMinutes(index);
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateProfile(observedAt),
                CreateProfile(observedAt) with { ProfileId = "secondary" },
            ],
            observedAt,
            Retention,
            cancellationToken);
      }

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          CreateNodeWindow(HistoryResolution.Raw, 100, 100, 4, 100),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      await Assert.That(
          history!.Profiles.Sum(profile => profile.Samples.Count))
          .IsEqualTo(4);
      await Assert.That(history.PointsTruncated).IsTrue();
      await Assert.That(history.ProfilePointLimit).IsEqualTo(100);
      await Assert.That(history.NodePointLimit).IsEqualTo(4);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Hourly_Peaks_Survive_Raw_Sample_Pruning(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("rollup");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = CreateRetention(
          TimeSpan.FromDays(7),
          TimeSpan.FromDays(90),
          TimeSpan.FromDays(30),
          1,
          100,
          100_000,
          100_000,
          100_000);
      for (var index = 0; index < 4; index++)
      {
        var observedAt = Origin.AddMinutes(index);
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [CreateProfile(observedAt, index, 1.5)],
            observedAt,
            retention,
            cancellationToken);
      }

      var raw = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(raw.Samples.Count).IsEqualTo(1);
      var hourly = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Hourly);
      await Assert.That(hourly.Rollups.Count).IsEqualTo(1);
      await Assert.That(hourly.Rollups[0].SampleCount).IsEqualTo(4);
      await Assert.That(hourly.Rollups[0].MaximumActiveSlots).IsEqualTo(3);
      await Assert.That(raw.Retention.DroppedSamples).IsEqualTo(3L);
      await Assert.That(raw.Retention.EarliestRetainedSample)
          .IsEqualTo(Origin.AddMinutes(3));
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Hourly_Buckets_Outside_The_Requested_Bounds_Are_Excluded(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("hourly-bounds");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      for (var index = 0; index < 3; index++)
      {
        var observedAt = Origin.AddHours(index);
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [CreateProfile(observedAt)],
            observedAt,
            Retention,
            cancellationToken);
      }

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          new HistoryWindow(
              Origin.AddHours(1),
              Origin.AddHours(2),
              HistoryResolution.Hourly,
              100,
              100,
              NodeDiagnosticLimit,
              NodePointLimit,
              NodeEventLimit,
              NodeDiagnosticLimit),
          Origin.AddHours(4),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var rollups = history!.Profiles[0].Rollups;
      await Assert.That(rollups.Count).IsEqualTo(1);
      await Assert.That(rollups[0].BucketStart)
          .IsEqualTo(Origin.AddHours(1));
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Overtaking_Journal_Reset_Reusing_Sequences_Starts_New_Epoch(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("overtaking");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [
                          CreateEvent(1, Origin),
                          CreateEvent(2, Origin),
                          CreateEvent(3, Origin),
                      ])),
          ],
          Origin,
          Retention,
          cancellationToken);
      var restarted = Origin.AddMinutes(1);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  restarted,
                  journal: CreateJournal(
                      "current",
                      [
                          CreateEvent(1, restarted),
                          CreateEvent(2, restarted),
                          CreateEvent(3, restarted),
                          CreateEvent(4, restarted),
                      ])),
          ],
          restarted,
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Journal.Epoch).IsEqualTo(1L);
      await Assert.That(history.Journal.EpochResets).IsEqualTo(1L);
      await Assert.That(history.Journal.StoredHighestSequence).IsEqualTo(4L);
      await Assert.That(history.Events.Count).IsEqualTo(7);
      await Assert.That(history.Journal.MissedEvents).IsEqualTo(0L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Journal_Epoch_Survives_A_Store_Restart(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("epochrestart");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, Origin), CreateEvent(2, Origin)])),
          ],
          Origin,
          Retention,
          cancellationToken);
      var restarted = Origin.AddMinutes(1);
      var replacementJournal = CreateJournal(
          "current",
          [CreateEvent(1, restarted), CreateEvent(2, restarted)]);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(restarted, journal: replacementJournal)],
          restarted,
          Retention,
          cancellationToken);

      var restartedStore = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          restartedStore,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin.AddMinutes(2),
                  journal: replacementJournal),
          ],
          Origin.AddMinutes(2),
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          restartedStore,
          nodeId,
          cancellationToken);
      await Assert.That(history.Journal.Epoch).IsEqualTo(1L);
      await Assert.That(history.Journal.EpochResets).IsEqualTo(1L);
      await Assert.That(history.Events.Count).IsEqualTo(4);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Absent_Diagnostic_Keys_Are_Not_Preserved_Forever(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("diagnosticage");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = new HistoryRetentionPolicy(
          TimeSpan.FromDays(14),
          TimeSpan.FromDays(90),
          TimeSpan.FromDays(30),
          TimeSpan.FromMinutes(30),
          100_000,
          100_000,
          100_000,
          100_000,
          100_000,
          100_000,
          100_000,
          1000,
          1_000_000,
          1_000_000,
          1_000_000,
          1_000_000,
          10_000,
          1_000,
          TimeSpan.FromMinutes(5),
          TimeSpan.FromDays(90));
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateAutoscalingProfile(Origin, "degraded", 3)],
          Origin,
          retention,
          cancellationToken);
      var later = Origin.AddHours(2);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateAutoscalingProfile(later, "healthy", 0)],
          later,
          retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Raw,
          Origin.AddHours(3));
      await Assert.That(
          history.SubsystemHealthChanges.Any(
              change => change.ObservedAt == Origin))
          .IsFalse();
      await Assert.That(
          history.CapacityDeficits.Any(
              deficit => deficit.ObservedAt == Origin))
          .IsFalse();
      await Assert.That(history.Retention.DroppedSubsystemHealthChanges)
          .IsGreaterThan(0L);
      await Assert.That(history.Retention.DroppedCapacityDeficits)
          .IsGreaterThan(0L);
      await Assert.That(history.Retention.EarliestRetainedSubsystemHealthChange)
          .IsEqualTo(later);
      await Assert.That(history.Retention.EarliestRetainedCapacityDeficit)
          .IsEqualTo(later);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Diagnostic_Key_Churn_Is_Bounded_By_The_Profile_Ceiling(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("diagnosticcap");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = new HistoryRetentionPolicy(
          TimeSpan.FromDays(14),
          TimeSpan.FromDays(90),
          TimeSpan.FromDays(30),
          TimeSpan.FromDays(30),
          100_000,
          100_000,
          2,
          100_000,
          100_000,
          100_000,
          100_000,
          1000,
          1_000_000,
          1_000_000,
          1_000_000,
          1_000_000,
          10_000,
          1_000,
          TimeSpan.FromMinutes(5),
          TimeSpan.FromDays(90));
      for (var index = 0; index < 5; index++)
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateAutoscalingProfile(
                    Origin.AddMinutes(index),
                    index % 2 == 0 ? "degraded" : "healthy",
                    index),
            ],
            Origin.AddMinutes(index),
            retention,
            cancellationToken);
      }

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.SubsystemHealthChanges.Count)
          .IsLessThanOrEqualTo(2);
      await Assert.That(history.CapacityDeficits.Count)
          .IsLessThanOrEqualTo(2);
      await Assert.That(history.Retention.DroppedSubsystemHealthChanges)
          .IsGreaterThan(0L);
      await Assert.That(history.Retention.DroppedCapacityDeficits)
          .IsGreaterThan(0L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Profile_Identifier_Churn_Is_Bounded_By_The_Node_Ceiling(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("profilecap");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = new HistoryRetentionPolicy(
          TimeSpan.FromDays(14),
          TimeSpan.FromDays(90),
          TimeSpan.FromDays(30),
          TimeSpan.FromDays(30),
          100_000,
          100_000,
          100_000,
          100_000,
          100_000,
          100_000,
          100_000,
          2,
          1_000_000,
          1_000_000,
          1_000_000,
          1_000_000,
          10_000,
          1_000,
          TimeSpan.FromMinutes(5),
          TimeSpan.FromDays(90));
      for (var index = 0; index < 5; index++)
      {
        var observedAt = Origin.AddMinutes(index);
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateProfile(observedAt) with
                {
                  ProfileId = $"profile-{index}",
                },
            ],
            observedAt,
            retention,
            cancellationToken);
      }

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          CreateWindow(HistoryResolution.Raw, 100, 100),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var retained = history!.Profiles
          .Where(profile => profile.Retention.HistoryExpiredAt is null)
          .ToList();
      await Assert.That(retained.Count).IsEqualTo(2);
      await Assert.That(retained.Count(
          profile => profile.ProfileId == "profile-3")).IsEqualTo(1);
      await Assert.That(retained.Count(
          profile => profile.ProfileId == "profile-4")).IsEqualTo(1);
      await Assert.That(history.Profiles.Count(
          profile => profile.ProfileId == "profile-0")).IsEqualTo(0);
      var nodeFloor = history.IncompletenessFloors.Single(
          floor => floor.Scope == "node");
      await Assert.That(nodeFloor.ExpiredProfiles).IsEqualTo(1L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Diagnostic_Limits_Are_Reported_With_Truncation(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("diagnosticlimits");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateAutoscalingProfile(Origin, "degraded", 3)],
          Origin,
          Retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateAutoscalingProfile(Origin.AddMinutes(1), "healthy", 0)],
          Origin.AddMinutes(1),
          Retention,
          cancellationToken);

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          new HistoryWindow(
              Origin.AddDays(-1),
              Origin.AddDays(1),
              HistoryResolution.Raw,
              100,
              100,
              1,
              NodePointLimit,
              NodeEventLimit,
              2),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      await Assert.That(history!.DiagnosticsTruncated).IsTrue();
      await Assert.That(history.ProfileSubsystemHealthLimit).IsEqualTo(1);
      await Assert.That(history.ProfileCapacityDeficitLimit).IsEqualTo(1);
      await Assert.That(history.NodeDiagnosticLimit).IsEqualTo(2);
      await Assert.That(history.ProfilePointLimit).IsEqualTo(100);
      await Assert.That(history.ProfileEventLimit).IsEqualTo(100);
      await Assert.That(history.NodePointLimit).IsEqualTo(NodePointLimit);
      await Assert.That(history.NodeEventLimit).IsEqualTo(NodeEventLimit);
      var profile = history.Profiles[0];
      await Assert.That(profile.SubsystemHealthTruncated).IsTrue();
      await Assert.That(profile.CapacityDeficitsTruncated).IsTrue();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Stale_Heartbeat_After_Retention_Reinserts_No_Sample(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("highwater");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var stale = CreateProfile(Origin);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [stale],
          Origin,
          Retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin.AddMinutes(1))],
          Origin.AddMinutes(1),
          Retention,
          cancellationToken);

      var aggressive = CreateRetention(
          TimeSpan.FromSeconds(30),
          TimeSpan.FromDays(90),
          TimeSpan.FromDays(30),
          100_000,
          20_000,
          500_000,
          200_000,
          200_000);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin.AddMinutes(2))],
          Origin.AddMinutes(2),
          aggressive,
          cancellationToken);

      var pruned = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Raw,
          Origin.AddHours(1));
      await Assert.That(pruned.Samples.Count).IsEqualTo(1);
      var prunedHourly = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Hourly,
          Origin.AddHours(1));
      var bucketBefore = prunedHourly.Rollups.Single().SampleCount;

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [stale],
          Origin.AddMinutes(3),
          Retention,
          cancellationToken);

      var replayed = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Raw,
          Origin.AddHours(1));
      await Assert.That(replayed.Samples.Count).IsEqualTo(1);
      await Assert.That(replayed.Samples[0].ObservedAt)
          .IsEqualTo(Origin.AddMinutes(2));
      var hourly = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Hourly,
          Origin.AddHours(1));
      await Assert.That(hourly.Rollups.Single().SampleCount)
          .IsEqualTo(bucketBefore);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Event_Replay_After_Retention_Reinserts_No_Event(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("eventreplay");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var journal = CreateJournal(
          "current",
          [CreateEvent(1, Origin), CreateEvent(2, Origin)]);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin, journal)],
          Origin,
          Retention,
          cancellationToken);

      var aggressive = CreateRetention(
          TimeSpan.FromDays(14),
          TimeSpan.FromDays(90),
          TimeSpan.FromSeconds(30),
          100_000,
          20_000,
          500_000,
          200_000,
          200_000);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin.AddMinutes(1))],
          Origin.AddMinutes(1),
          aggressive,
          cancellationToken);

      var pruned = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(pruned.Events.Count).IsEqualTo(0);
      await Assert.That(pruned.Journal.EpochResets).IsEqualTo(0L);

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin.AddMinutes(2), journal)],
          Origin.AddMinutes(2),
          Retention,
          cancellationToken);

      var replayed = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(replayed.Events.Count).IsEqualTo(0);
      await Assert.That(replayed.Journal.EpochResets).IsEqualTo(0L);
      await Assert.That(replayed.Journal.MissedEvents).IsEqualTo(0L);
      await Assert.That(replayed.Retention.DroppedEvents).IsEqualTo(2L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Tied_Timestamps_Retain_Exactly_The_Configured_Newest_Count(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("tiedtimestamps");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      for (var index = 0; index < 5; index++)
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateProfile(Origin.AddSeconds(index)) with
                {
                  ProfileId = $"profile-{index}",
                },
            ],
            Origin,
            Retention,
            cancellationToken);
      }

      var bounded = CreateRetention(
          TimeSpan.FromDays(14),
          TimeSpan.FromDays(90),
          TimeSpan.FromDays(30),
          100_000,
          20_000,
          3,
          200_000,
          200_000);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [],
          Origin.AddMinutes(1),
          bounded,
          cancellationToken);

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          CreateWindow(HistoryResolution.Raw, 100, 100),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var retained = history!.Profiles.Sum(profile => profile.Samples.Count);
      await Assert.That(retained).IsEqualTo(3);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Same_Timestamp_Diagnostic_Change_Is_Recorded_Without_Aborting(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("diagnosticupdate");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateAutoscalingProfile(Origin, "degraded", 1)],
          Origin,
          Retention,
          cancellationToken);
      var changed = CreateAutoscalingProfile(Origin, "failed", 4) with
      {
        ObservedAt = Origin.AddSeconds(30),
      };
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [changed],
          Origin.AddSeconds(30),
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      var docker = history.SubsystemHealthChanges
          .Where(change => change.Subsystem == "docker")
          .ToList();
      await Assert.That(docker.Count).IsEqualTo(1);
      await Assert.That(docker[0].State).IsEqualTo("failed");
      await Assert.That(docker[0].ConsecutiveFailures).IsEqualTo(4);
      await Assert.That(history.Samples.Count).IsEqualTo(2);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Implausibly_Future_Diagnostic_Rejects_The_Profile_Heartbeat(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("futurediagnostic");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var skewed = CreateAutoscalingProfile(
          Origin.AddHours(2),
          "degraded",
          1) with
      {
        ObservedAt = Origin,
      };
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [skewed],
          Origin,
          Retention,
          cancellationToken);

      var history = await store.GetProfileHistoryAsync(
          "tenant",
          nodeId,
          "default",
          CreateWindow(HistoryResolution.Raw, 100, 100),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var profile = history!.Profiles.Single();
      await Assert.That(profile.Samples.Count).IsEqualTo(0);
      await Assert.That(profile.SubsystemHealthChanges.Count).IsEqualTo(0);
      await Assert.That(profile.Retention.RejectedFutureSamples)
          .IsEqualTo(1L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Node_Diagnostic_Budget_Is_Shared_By_All_Collections(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("diagnosticbudget");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      for (var index = 0; index < 4; index++)
      {
        var observation = CreateAutoscalingProfile(
            Origin.AddMinutes(index),
            index % 2 == 0 ? "degraded" : "healthy",
            index) with
        {
          Update = new ManagerWorkerUpdateState(
              "rolling",
              $"ghcr.io/example/runner:{index}",
              $"sha256:{new string((char)('1' + index), 64)}",
              new string((char)('a' + index), 64),
              1,
              1,
              null),
        };
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [observation],
            Origin.AddMinutes(index),
            Retention,
            cancellationToken);
      }

      var history = await store.GetProfileHistoryAsync(
          "tenant",
          nodeId,
          "default",
          new HistoryWindow(
              Origin.AddDays(-1),
              Origin.AddDays(1),
              HistoryResolution.Raw,
              100,
              100,
              100,
              NodePointLimit,
              NodeEventLimit,
              3),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var profile = history!.Profiles.Single();
      var returned = profile.SubsystemHealthChanges.Count +
          profile.CapacityDeficits.Count +
          profile.WorkerUpdateChanges.Count;
      await Assert.That(returned).IsEqualTo(3);
      await Assert.That(profile.WorkerUpdateChanges.Count).IsGreaterThan(0);
      await Assert.That(history.DiagnosticsTruncated).IsTrue();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Omitted_Profile_Is_Reported_As_Truncated_Rather_Than_Complete(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("zeroreturned");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      for (var index = 0; index < 3; index++)
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateProfile(Origin.AddMinutes(index)) with
                {
                  ProfileId = "older",
                },
                CreateProfile(Origin.AddMinutes(index + 10)) with
                {
                  ProfileId = "newer",
                },
            ],
            Origin.AddMinutes(index + 10),
            Retention,
            cancellationToken);
      }

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          CreateNodeWindow(HistoryResolution.Raw, 100, 100, 3, 100),
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var older = history!.Profiles.Single(
          profile => profile.ProfileId == "older");
      await Assert.That(older.Samples.Count).IsEqualTo(0);
      await Assert.That(older.PointsTruncated).IsTrue();
      await Assert.That(history.PointsTruncated).IsTrue();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Stale_Heartbeat_Does_Not_Reset_The_Epoch_Or_Replay_An_Older_Ring(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("staleheartbeat");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var current = Origin.AddMinutes(5);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  current,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(9, current), CreateEvent(10, current)])),
          ],
          current,
          Retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [
                          CreateEvent(1, Origin),
                          CreateEvent(2, current.AddHours(1)),
                      ])),
          ],
          current.AddSeconds(15),
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Journal.Epoch).IsEqualTo(0L);
      await Assert.That(history.Journal.EpochResets).IsEqualTo(0L);
      await Assert.That(history.Journal.StoredHighestSequence).IsEqualTo(10L);
      await Assert.That(history.Events.Count).IsEqualTo(2);
      await Assert.That(history.Samples.Count).IsEqualTo(1);
      await Assert.That(history.Journal.MissedEvents).IsEqualTo(0L);
      await Assert.That(history.Journal.ManagerDroppedEvents).IsEqualTo(0);
      await Assert.That(history.Retention.DroppedEvents).IsEqualTo(0L);
      await Assert.That(history.Retention.DroppedSamples).IsEqualTo(0L);
      await Assert.That(history.Journal.RejectedFutureEvents).IsEqualTo(0L);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_event_identities;",
          cancellationToken))
          .IsEqualTo(2L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Future_Event_Timestamp_Rejects_The_Whole_Profile_Heartbeat(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("futureevent");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, Origin)])),
          ],
          Origin,
          Retention,
          cancellationToken);
      var later = Origin.AddMinutes(1);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  later,
                  journal: CreateJournal(
                      "current",
                      [
                          CreateEvent(2, later),
                          CreateEvent(3, later.AddHours(1)),
                      ])),
          ],
          later,
          Retention,
          cancellationToken);

      var rejected = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(rejected.Samples.Count).IsEqualTo(1);
      await Assert.That(rejected.Events.Count).IsEqualTo(1);
      await Assert.That(rejected.Journal.StoredHighestSequence).IsEqualTo(1L);
      await Assert.That(rejected.Journal.RejectedFutureEvents).IsEqualTo(1L);
      await Assert.That(rejected.Journal.Epoch).IsEqualTo(0L);
      await Assert.That(rejected.Retention.EarliestRetainedSample)
          .IsNotNull();

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin.AddMinutes(2),
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(2, Origin.AddMinutes(2))])),
          ],
          Origin.AddMinutes(2),
          Retention,
          cancellationToken);

      var recovered = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(recovered.Samples.Count).IsEqualTo(2);
      await Assert.That(recovered.Journal.StoredHighestSequence).IsEqualTo(2L);
      await Assert.That(recovered.Journal.EpochResets).IsEqualTo(0L);
      await Assert.That(recovered.Journal.RejectedFutureEvents).IsEqualTo(1L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Unknown_Event_Fingerprint_Still_Detects_A_Conflicting_Reset(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("unknownfingerprint");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, Origin), CreateEvent(2, Origin)])),
          ],
          Origin,
          Retention,
          cancellationToken);
      await ExecuteAsync(
          connectionFactory,
          "UPDATE profile_event_identities SET fingerprint = '';",
          cancellationToken);

      var restarted = Origin.AddMinutes(1);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  restarted,
                  journal: CreateJournal(
                      "current",
                      [
                          CreateEvent(1, restarted),
                          CreateEvent(2, restarted),
                      ])),
          ],
          restarted,
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Journal.Epoch).IsEqualTo(1L);
      await Assert.That(history.Journal.EpochResets).IsEqualTo(1L);
      await Assert.That(history.Events.Count).IsEqualTo(4);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Event_Identities_Survive_Profile_Eviction(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("identitysurvival");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = CreateRetention(
          TimeSpan.FromDays(14),
          TimeSpan.FromDays(90),
          TimeSpan.FromDays(30),
          100_000,
          20_000,
          500_000,
          200_000,
          200_000) with
      {
        MaximumProfilesPerNode = 1,
      };
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  Origin,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, Origin), CreateEvent(2, Origin)])),
          ],
          Origin,
          retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(Origin.AddMinutes(1)) with
              {
                ProfileId = "other",
              },
          ],
          Origin.AddMinutes(1),
          retention,
          cancellationToken);

      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_event_identities " +
              "WHERE profile_id = 'default';",
          cancellationToken))
          .IsEqualTo(2L);

      var restarted = Origin.AddMinutes(2);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  restarted,
                  journal: CreateJournal(
                      "current",
                      [
                          CreateEvent(1, restarted),
                          CreateEvent(2, restarted),
                      ])),
          ],
          restarted,
          retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Journal.EpochResets).IsEqualTo(1L);
      await Assert.That(history.Journal.Epoch).IsEqualTo(1L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Rapid_Multi_Node_Churn_Never_Exceeds_The_Database_Cap(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("churncaps");
    try
    {
      var (connectionFactory, firstNodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = Retention with
      {
        MaximumSamplesPerDatabase = 5,
        MaximumEventsPerDatabase = 5,
      };
      var nodeIds = new List<Guid> { firstNodeId };
      for (var index = 0; index < 5; index++)
      {
        nodeIds.Add(await EnrollNodeAsync(
            connectionFactory,
            $"churn-{index}",
            cancellationToken));
      }

      for (var round = 0; round < 4; round++)
      {
        foreach (var nodeId in nodeIds)
        {
          var observedAt = Origin.AddSeconds((round * 30) + 1);
          await FleetStorageTestTransactions.AppendAsync(
              store,
              connectionFactory,
              nodeId,
              [
                  CreateProfile(
                      observedAt,
                      journal: CreateJournal(
                          "current",
                          [CreateEvent(round + 1, observedAt)])),
              ],
              observedAt,
              retention,
              cancellationToken);
          await Assert.That(await CountAsync(
              connectionFactory,
              "SELECT COUNT(*) FROM profile_telemetry_samples;",
              cancellationToken))
              .IsLessThanOrEqualTo(5L);
          await Assert.That(await CountAsync(
              connectionFactory,
              "SELECT COUNT(*) FROM profile_manager_events;",
              cancellationToken))
              .IsLessThanOrEqualTo(5L);
        }
      }
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Global_Sweep_Applies_Lowered_Node_Caps_To_Abandoned_Nodes(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("abandonedcaps");
    try
    {
      var (connectionFactory, abandonedNodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var activeNodeId = await EnrollNodeAsync(
          connectionFactory,
          "active",
          cancellationToken);
      for (var index = 0; index < 6; index++)
      {
        var observedAt = Origin.AddMinutes(index);
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            abandonedNodeId,
            [CreateProfile(observedAt)],
            observedAt,
            Retention,
            cancellationToken);
      }

      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_telemetry_samples " +
              $"WHERE node_id = '{abandonedNodeId:D}';",
          cancellationToken))
          .IsEqualTo(6L);

      var lowered = Retention with
      {
        MaximumSamplesPerNode = 2,
      };
      var swept = Origin.AddHours(2);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          activeNodeId,
          [CreateProfile(swept)],
          swept,
          lowered,
          cancellationToken);

      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_telemetry_samples " +
              $"WHERE node_id = '{abandonedNodeId:D}';",
          cancellationToken))
          .IsEqualTo(2L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Tied_Timestamps_Are_Retained_Deterministically_Across_Nodes(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("tiedkeys");
    try
    {
      var (connectionFactory, firstNodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var secondNodeId = await EnrollNodeAsync(
          connectionFactory,
          "tied",
          cancellationToken);
      var retention = Retention with
      {
        MaximumSamplesPerDatabase = 1,
      };
      foreach (var nodeId in new[] { firstNodeId, secondNodeId })
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [CreateProfile(Origin)],
            Origin,
            retention,
            cancellationToken);
      }

      var expected = string.CompareOrdinal(
          firstNodeId.ToString("D"),
          secondNodeId.ToString("D")) > 0
          ? firstNodeId
          : secondNodeId;
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_telemetry_samples;",
          cancellationToken))
          .IsEqualTo(1L);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_telemetry_samples " +
              $"WHERE node_id = '{expected:D}';",
          cancellationToken))
          .IsEqualTo(1L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Upgraded_High_Water_Survives_Pruned_Raw_Samples(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("highwaterupgrade");
    try
    {
      var (connectionFactory, nodeId) =
          await CreateVersionSevenEnrolledNodeAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var latest = Origin.AddMinutes(3).AddMilliseconds(250);
      await SeedVersionSevenCursorAsync(
          connectionFactory,
          nodeId,
          latest,
          cancellationToken);
      await SeedVersionSevenRollupAsync(
          connectionFactory,
          nodeId,
          Origin,
          4,
          cancellationToken);
      await ExecuteAsync(
          connectionFactory,
          $"""
          INSERT INTO profiles (
              node_id,
              profile_id,
              payload_hash,
              payload_json,
              observed_at)
          VALUES (
              '{nodeId:D}',
              'default',
              'payload-hash',
              '[]',
              '{latest:O}');
          """,
          cancellationToken);

      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);

      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_history_cursors " +
              "WHERE sample_high_water IS NOT NULL;",
          cancellationToken))
          .IsEqualTo(1L);

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(latest)],
          latest.AddMinutes(1),
          Retention,
          cancellationToken);

      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_telemetry_samples;",
          cancellationToken))
          .IsEqualTo(0L);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COALESCE(SUM(sample_count), 0) " +
              "FROM profile_telemetry_rollups;",
          cancellationToken))
          .IsEqualTo(4L);

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(latest.AddMilliseconds(500))],
          latest.AddMinutes(2),
          Retention,
          cancellationToken);

      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_telemetry_samples;",
          cancellationToken))
          .IsEqualTo(1L);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COALESCE(SUM(sample_count), 0) " +
              "FROM profile_telemetry_rollups;",
          cancellationToken))
          .IsEqualTo(5L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Migration_Nine_Uses_The_Rollup_End_When_No_Profile_Projection_Remains(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("rollup-only-highwater");
    try
    {
      var (connectionFactory, nodeId) =
          await CreateVersionSevenEnrolledNodeAsync(
              databasePath,
              cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var observedAt = Origin.AddMinutes(47);
      await SeedVersionSevenCursorAsync(
          connectionFactory,
          nodeId,
          observedAt,
          cancellationToken);
      await SeedVersionSevenRollupAsync(
          connectionFactory,
          nodeId,
          Origin,
          4,
          cancellationToken);

      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(observedAt)],
          observedAt.AddMinutes(1),
          Retention,
          cancellationToken);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_telemetry_samples;",
          cancellationToken))
          .IsEqualTo(0L);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COALESCE(SUM(sample_count), 0) " +
              "FROM profile_telemetry_rollups;",
          cancellationToken))
          .IsEqualTo(4L);

      var nextHour = Origin.AddHours(1).AddMinutes(1);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(nextHour)],
          nextHour,
          Retention,
          cancellationToken);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_telemetry_samples;",
          cancellationToken))
          .IsEqualTo(1L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Coarse_Incompleteness_Floor_Bounds_Stale_Returning_Profile_And_Node_Floors(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("coarse-floor-gate");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var floorTime = Origin.AddHours(1);
      await ExecuteAsync(
          connectionFactory,
          $"""
          INSERT INTO history_incompleteness_floors (
              scope, node_id, earliest_expired_at, latest_expired_at,
              expired_profiles, dropped_samples, dropped_rollups, dropped_events,
              dropped_subsystem_health, dropped_capacity_deficits)
          VALUES
              ('database', '', '{floorTime.AddMinutes(-2):O}', '{floorTime:O}', 3, 3, 0, 0, 0, 0),
              ('node', '{nodeId:D}', '{floorTime:O}', '{floorTime:O}', 1, 1, 0, 0, 0, 0),
              ('node', 'node-b', '{floorTime.AddMinutes(-1):O}', '{floorTime.AddMinutes(-1):O}', 1, 1, 0, 0, 0, 0),
              ('node', 'node-c', '{floorTime.AddMinutes(-2):O}', '{floorTime.AddMinutes(-2):O}', 1, 1, 0, 0, 0, 0);
          """,
          cancellationToken);
      var bounded = Retention with
      {
        MaximumHistoryNodes = 1,
      };

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(floorTime.AddMinutes(-1))],
          floorTime.AddMinutes(1),
          bounded,
          cancellationToken);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_telemetry_samples;",
          cancellationToken))
          .IsEqualTo(0L);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM history_incompleteness_floors " +
              "WHERE scope = 'node';",
          cancellationToken))
          .IsEqualTo(1L);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT expired_profiles FROM history_incompleteness_floors " +
              "WHERE scope = 'database';",
          cancellationToken))
          .IsEqualTo(3L);

      var history = await store.GetNodeHistoryAsync(
          "tenant",
          nodeId,
          new HistoryWindow(
              floorTime,
              floorTime.AddHours(1),
              HistoryResolution.Raw,
              100,
              100,
              100,
              100,
              100,
              100),
          floorTime.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      await Assert.That(history!.IncompletenessFloors.Count).IsEqualTo(2);
      var databaseFloor = history.IncompletenessFloors.Single(
          floor => floor.Scope == "database");
      var nodeFloor = history.IncompletenessFloors.Single(
          floor => floor.Scope == "node");
      await Assert.That(databaseFloor.ExpiredProfiles).IsEqualTo(3L);
      await Assert.That(nodeFloor.LatestExpiredAt).IsEqualTo(floorTime);
      var expiredProfile = history.Profiles.Single(
          profile => profile.ProfileId == "default");
      await Assert.That(expiredProfile.Journal.Epoch).IsEqualTo(1L);
      await Assert.That(expiredProfile.Journal.EpochResets).IsEqualTo(1L);
      await Assert.That(expiredProfile.Retention.HistoryExpiredAt)
          .IsEqualTo(floorTime);

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(floorTime.AddMinutes(2))],
          floorTime.AddMinutes(2),
          bounded,
          cancellationToken);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM profile_telemetry_samples;",
          cancellationToken))
          .IsEqualTo(1L);
      var resumed = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Raw,
          floorTime.AddHours(1));
      await Assert.That(resumed.Retention.HistoryExpiredAt).IsNull();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Orphaned_Node_Floor_Is_Removed_Without_Double_Counting_The_Database_Floor(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("orphan-floor");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var floorTime = Origin.AddHours(1);
      await ExecuteAsync(
          connectionFactory,
          $"""
          INSERT INTO history_incompleteness_floors (
              scope, node_id, earliest_expired_at, latest_expired_at,
              expired_profiles, dropped_samples, dropped_rollups, dropped_events,
              dropped_subsystem_health, dropped_capacity_deficits)
          VALUES
              ('database', '', '{floorTime:O}', '{floorTime:O}', 1, 2, 3, 4, 5, 6),
              ('node', 'orphaned-node', '{floorTime:O}', '{floorTime:O}', 1, 2, 3, 4, 5, 6);
          """,
          cancellationToken);

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(floorTime.AddMinutes(1))],
          floorTime.AddMinutes(1),
          Retention,
          cancellationToken);

      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT COUNT(*) FROM history_incompleteness_floors " +
              "WHERE scope = 'node';",
          cancellationToken))
          .IsEqualTo(0L);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT expired_profiles FROM history_incompleteness_floors " +
              "WHERE scope = 'database';",
          cancellationToken))
          .IsEqualTo(1L);
      await Assert.That(await CountAsync(
          connectionFactory,
          "SELECT dropped_capacity_deficits " +
              "FROM history_incompleteness_floors " +
              "WHERE scope = 'database';",
          cancellationToken))
          .IsEqualTo(6L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  private static async Task<ProfileHistory> ReadProfileHistoryAsync(
      SqliteFleetHistoryStore store,
      Guid nodeId,
      CancellationToken cancellationToken) =>
      await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Raw);

  private static async Task<ProfileHistory> ReadProfileHistoryAsync(
      SqliteFleetHistoryStore store,
      Guid nodeId,
      CancellationToken cancellationToken,
      HistoryResolution resolution) =>
      await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          resolution,
          Origin.AddHours(1));

  private static async Task<ProfileHistory> ReadProfileHistoryAsync(
      SqliteFleetHistoryStore store,
      Guid nodeId,
      CancellationToken cancellationToken,
      HistoryResolution resolution,
      DateTimeOffset generatedAt) =>
      await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          resolution,
          generatedAt,
          100);

  private static async Task<ProfileHistory> ReadProfileHistoryAsync(
      SqliteFleetHistoryStore store,
      Guid nodeId,
      CancellationToken cancellationToken,
      HistoryResolution resolution,
      DateTimeOffset generatedAt,
      int pointLimit)
  {
    var response = await store.GetProfileHistoryAsync(
        "tenant",
        nodeId,
        "default",
        CreateWindow(resolution, pointLimit, 100),
        generatedAt,
        cancellationToken);
    if (response is null || response.Profiles.Count != 1)
    {
      throw new InvalidOperationException(
          "The tenant node must expose exactly one retained profile history.");
    }

    return response.Profiles[0];
  }

  private static HistoryWindow CreateWindow(
      HistoryResolution resolution,
      int pointLimit,
      int eventLimit) =>
      new(
          Origin.AddDays(-1),
          Origin.AddDays(1),
          resolution,
          pointLimit,
          eventLimit,
          NodeDiagnosticLimit,
          NodePointLimit,
          NodeEventLimit,
          NodeDiagnosticLimit);

  private static HistoryWindow CreateNodeWindow(
      HistoryResolution resolution,
      int pointLimit,
      int eventLimit,
      int nodePointLimit,
      int nodeEventLimit) =>
      new(
          Origin.AddDays(-1),
          Origin.AddDays(1),
          resolution,
          pointLimit,
          eventLimit,
          NodeDiagnosticLimit,
          nodePointLimit,
          nodeEventLimit,
          NodeDiagnosticLimit);

  private static ManagerEvent CreateEvent(
      long sequence,
      DateTimeOffset observedAt) =>
      new(
          sequence,
          "manager-instance",
          observedAt,
          "docker",
          "worker-start",
          "slot-1",
          "failed",
          120,
          2,
          3,
          observedAt.AddSeconds(30),
          "image-pull-backoff",
          "manager evidence");

  private static ManagerOperationJournal CreateJournal(
      string status,
      IReadOnlyList<ManagerEvent> events) =>
      CreateJournal(
          status,
          events,
          events.Count == 0 ? null : events[^1].Sequence,
          0);

  private static ManagerOperationJournal CreateJournal(
      string status,
      IReadOnlyList<ManagerEvent> events,
      long? highestSequence,
      int droppedEvents) =>
      new(
          status,
          64,
          highestSequence,
          droppedEvents,
          events);

  private static ManagerObservedState CreateProfile(
      DateTimeOffset observedAt) =>
      CreateProfile(observedAt, 2, 1.5, null);

  private static ManagerObservedState CreateProfile(
      DateTimeOffset observedAt,
      ManagerOperationJournal? journal) =>
      CreateProfile(observedAt, 2, 1.5, journal);

  private static ManagerObservedState CreateProfile(
      DateTimeOffset observedAt,
      int activeSlots,
      double managerCpuCores) =>
      CreateProfile(observedAt, activeSlots, managerCpuCores, null);

  private static ManagerObservedState CreateProfile(
      DateTimeOffset observedAt,
      int activeSlots,
      double managerCpuCores,
      ManagerOperationJournal? journal) =>
      new(
          1,
          12,
          "default",
          "manager-instance",
          "running",
          observedAt,
          "repository",
          7,
          new string('a', 64),
          "accepted",
          4,
          activeSlots,
          0,
          [
              new ObservedSlotState(
                  "slot-1",
                  "owner/repo",
                  true,
                  true,
                  "running",
                  0,
                  0,
                  observedAt,
                  new ResourceUsage(
                      1.5,
                      1_048_576,
                      12,
                      2048,
                      1024,
                      4096,
                      512),
                  "busy",
                  "target-1",
                  "connected",
                  $"sha256:{new string('b', 64)}",
                  new WorkerLastExitDiagnostic(
                      observedAt,
                      "oom-killed",
                      137,
                      9,
                      true,
                      "docker-inspect")),
          ],
          new ManagerResourceTelemetry(
              observedAt,
              "available",
              new HostResourceCapacity(8, 34_359_738_368),
              new ResourceUsage(managerCpuCores, 268_435_456, 24)),
          4,
          null,
          3,
          null,
          journal,
          null,
          new ManagerCapacityEvidence(
              new CapacityDeficitEvidence(
                  observedAt,
                  "current",
                  4,
                  2,
                  1,
                  0,
                  0,
                  3,
                  2,
                  1,
                  "image-pull-backoff",
                  "manager evidence"),
              []));

  private static ManagerObservedState CreateAutoscalingProfile(
      DateTimeOffset observedAt,
      string dockerState,
      int consecutiveFailures) =>
      CreateProfile(observedAt) with
      {
        SubsystemHealth = new ManagerSubsystemHealth(
            new SubsystemHealthSummary(
                dockerState,
                observedAt,
                consecutiveFailures,
                consecutiveFailures == 0 ? null : Origin.AddMinutes(1),
                new SubsystemOperationEvidence(
                    "worker-start",
                    Origin,
                    120,
                    "started",
                    null),
                consecutiveFailures == 0
                    ? null
                    : new SubsystemOperationEvidence(
                        "worker-start",
                        Origin,
                        250,
                        "image-pull-backoff",
                        "docker evidence")),
            new SubsystemHealthSummary(
                "healthy",
                observedAt,
                0,
                null,
                new SubsystemOperationEvidence(
                    "scale-set-poll",
                    Origin,
                    80,
                    "polled",
                    null),
                null)),
        CapacityEvidence = new ManagerCapacityEvidence(
            null,
            [
                new TargetCapacityDeficitEvidence(
                    "owner/repo-a",
                    "owner/repo-a",
                    observedAt,
                    "current",
                    4,
                    2,
                    1,
                    0,
                    0,
                    3,
                    consecutiveFailures,
                    1,
                    "image-pull-backoff",
                    "docker evidence"),
                new TargetCapacityDeficitEvidence(
                    "owner/repo-b",
                    "owner/repo-b",
                    observedAt,
                    "current",
                    6,
                    4,
                    0,
                    0,
                    0,
                    4,
                    2,
                    0,
                    "scale-set-throttled",
                    "github evidence"),
            ]),
      };

  private static HistoryRetentionPolicy CreateRetention(
      TimeSpan sampleRetention,
      TimeSpan rollupRetention,
      TimeSpan eventRetention,
      int maximumSamplesPerProfile,
      int maximumEventsPerProfile,
      int maximumSamplesPerNode,
      int maximumEventsPerNode,
      int maximumRollupsPerNode) =>
      new(
          sampleRetention,
          rollupRetention,
          eventRetention,
          eventRetention,
          maximumSamplesPerProfile,
          maximumEventsPerProfile,
          100_000,
          maximumSamplesPerNode,
          maximumEventsPerNode,
          maximumRollupsPerNode,
          100_000,
          1000,
          1_000_000,
          1_000_000,
          1_000_000,
          1_000_000,
          10_000,
          1_000,
          TimeSpan.FromMinutes(5),
          TimeSpan.FromDays(90));

  private static string CreateDatabasePath(string label) =>
      Path.Combine(
          Path.GetTempPath(),
          $"pitcrew-history-{label}-{Guid.NewGuid():N}.db");

  private static void Cleanup(string databasePath)
  {
    SqliteConnection.ClearAllPools();
    DashboardTestCleanup.DeleteDatabase(databasePath);
  }

  private static async Task<long> CountAsync(
      SqliteConnectionFactory connectionFactory,
      string sql,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    var value = await command.ExecuteScalarAsync(cancellationToken);
    return Convert.ToInt64(value, CultureInfo.InvariantCulture);
  }

  private static async Task ExecuteAsync(
      SqliteConnectionFactory connectionFactory,
      string sql,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<(
      SqliteConnectionFactory ConnectionFactory,
      Guid NodeId)> CreateEnrolledNodeAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var connectionFactory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
        cancellationToken);
    var owner = new DashboardUser(
        "1",
        "owner",
        "Owner",
        null);
    await new SqliteAccessStore(connectionFactory).EnsureTenantOwnerAsync(
        "tenant",
        "Tenant",
        owner,
        Origin,
        cancellationToken);
    return (
        connectionFactory,
        await EnrollNodeAsync(
            connectionFactory,
            "connector",
            cancellationToken));
  }

  private static async Task<(
      SqliteConnectionFactory ConnectionFactory,
      Guid NodeId)> CreateVersionSevenEnrolledNodeAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var connectionFactory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    await SqliteMigrationTestDatabase.ApplyThroughAsync(
        connectionFactory,
        7,
        cancellationToken);
    var owner = new DashboardUser(
        "1",
        "owner",
        "Owner",
        null);
    await new SqliteAccessStore(connectionFactory).EnsureTenantOwnerAsync(
        "tenant",
        "Tenant",
        owner,
        Origin,
        cancellationToken);
    return (
        connectionFactory,
        await EnrollNodeAsync(
            connectionFactory,
            "connector",
            cancellationToken));
  }

  private static Task SeedVersionSevenCursorAsync(
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      DateTimeOffset updatedAt,
      CancellationToken cancellationToken) =>
      ExecuteAsync(
          connectionFactory,
          $"""
          INSERT INTO profile_history_cursors (
              node_id,
              profile_id,
              journal_status,
              journal_capacity,
              epoch,
              epoch_resets,
              manager_dropped_events,
              missed_events,
              dropped_samples,
              dropped_rollups,
              dropped_events,
              dropped_subsystem_health,
              dropped_capacity_deficits,
              rejected_future_samples,
              rejected_future_events,
              updated_at)
          VALUES (
              '{nodeId:D}',
              'default',
              'current',
              64,
              0,
              0,
              0,
              0,
              0,
              0,
              0,
              0,
              0,
              0,
              0,
              '{updatedAt:O}');
          """,
          cancellationToken);

  private static Task SeedVersionSevenRollupAsync(
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      DateTimeOffset bucketStart,
      int sampleCount,
      CancellationToken cancellationToken) =>
      ExecuteAsync(
          connectionFactory,
          $"""
          INSERT INTO profile_telemetry_rollups (
              node_id,
              profile_id,
              bucket_start,
              sample_count,
              max_desired_slots,
              max_active_slots,
              max_draining_slots,
              max_local_running_workers,
              max_exit_reports,
              max_adverse_exit_reports)
          VALUES (
              '{nodeId:D}',
              'default',
              '{bucketStart:O}',
              {sampleCount},
              4,
              2,
              0,
              2,
              0,
              0);
          """,
          cancellationToken);

  private static async Task<Guid> EnrollNodeAsync(
      SqliteConnectionFactory connectionFactory,
      string label,
      CancellationToken cancellationToken)
  {
    var store = new SqliteFleetStore(connectionFactory);
    await store.CreateEnrollmentCodeAsync(
        Guid.NewGuid(),
        "tenant",
        $"code-hash-{label}",
        "Enrollment",
        "1",
        Origin,
        Origin.AddMinutes(10),
        cancellationToken);
    var enrollment = await store.RedeemEnrollmentCodeAsync(
        $"code-hash-{label}",
        $"connector-instance-{label}",
        $"Connector {label}",
        $"credential-hash-{label}",
        Origin,
        cancellationToken);
    return enrollment.NodeId ??
        throw new InvalidOperationException(
            "Enrollment did not return a node ID.");
  }
}
