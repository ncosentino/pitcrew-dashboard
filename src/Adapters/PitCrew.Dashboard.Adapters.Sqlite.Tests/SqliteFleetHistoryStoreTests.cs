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
      await Assert.That(retained.Count).IsLessThanOrEqualTo(2);
      await Assert.That(
          retained.Any(profile => profile.ProfileId == "profile-4"))
          .IsTrue();
      await Assert.That(history.Profiles.Any(profile =>
          profile.ProfileId == "profile-0" &&
          profile.Retention.HistoryExpiredAt is not null))
          .IsTrue();
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
  public async Task Node_Diagnostic_Budget_Is_Shared_By_Both_Collections(
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
          profile.CapacityDeficits.Count;
      await Assert.That(returned).IsEqualTo(3);
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
  public async Task Stale_Heartbeat_Journal_Does_Not_Start_A_New_Epoch(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("staleevents");
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

      var stale = Origin.AddMinutes(-1);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  stale,
                  journal: CreateJournal(
                      "current",
                      [CreateEvent(1, stale)])),
          ],
          Origin.AddSeconds(15),
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken);
      await Assert.That(history.Journal.Epoch).IsEqualTo(0L);
      await Assert.That(history.Journal.EpochResets).IsEqualTo(0L);
      await Assert.That(history.Journal.StoredHighestSequence).IsEqualTo(2L);
      await Assert.That(history.Journal.ManagerHighestSequence).IsEqualTo(2L);
      await Assert.That(history.Events.Count).IsEqualTo(2);
      await Assert.That(history.Samples).HasSingleItem();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Database_Sample_Ceiling_Is_Enforced_On_Every_Append(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("databasecap");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = Retention with
      {
        MaximumSamplesPerDatabase = 3,
        GlobalSweepInterval = TimeSpan.FromDays(30),
      };
      for (var index = 0; index < 6; index++)
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [CreateProfile(Origin.AddMinutes(index))],
            Origin.AddMinutes(index),
            retention,
            cancellationToken);
      }

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Raw,
          Origin.AddHours(1));
      await Assert.That(history.Samples.Count).IsEqualTo(3);
      await Assert.That(history.Samples[0].ObservedAt)
          .IsEqualTo(Origin.AddMinutes(3));
      await Assert.That(history.Retention.DroppedSamples).IsEqualTo(3L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Abandoned_Node_Is_Bounded_By_Its_Own_Node_Ceiling(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("abandoned");
    try
    {
      var (connectionFactory, abandonedNodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var activeNodeId = await EnrollNodeAsync(
          connectionFactory,
          "second",
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      for (var index = 0; index < 4; index++)
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            abandonedNodeId,
            [CreateProfile(Origin.AddMinutes(index))],
            Origin.AddMinutes(index),
            Retention,
            cancellationToken);
      }

      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          activeNodeId,
          [CreateProfile(Origin.AddMinutes(10))],
          Origin.AddMinutes(10),
          Retention with
          {
            MaximumSamplesPerNode = 2,
            GlobalSweepInterval = TimeSpan.Zero,
          },
          cancellationToken);

      var abandoned = await ReadProfileHistoryAsync(
          store,
          abandonedNodeId,
          cancellationToken,
          HistoryResolution.Raw,
          Origin.AddHours(1));
      await Assert.That(abandoned.Samples.Count).IsEqualTo(2);
      await Assert.That(abandoned.Samples[0].ObservedAt)
          .IsEqualTo(Origin.AddMinutes(2));
      await Assert.That(abandoned.Retention.DroppedSamples).IsEqualTo(2L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Reused_Sequence_After_History_Expiry_Starts_A_New_Epoch(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("expiredidentity");
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
          [CreateProfile(Origin.AddMinutes(1)) with { ProfileId = "other" }],
          Origin.AddMinutes(1),
          Retention with { MaximumProfilesPerNode = 1 },
          cancellationToken);

      var reused = Origin.AddMinutes(2);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [
              CreateProfile(
                  reused,
                  journal: CreateJournal(
                      "current",
                      [
                          CreateEvent(1, reused) with
                          {
                            Reason = "scale-set-throttled",
                          },
                      ])),
          ],
          reused,
          Retention,
          cancellationToken);

      var history = await ReadProfileHistoryAsync(
          store,
          nodeId,
          cancellationToken,
          HistoryResolution.Raw,
          Origin.AddHours(1));
      await Assert.That(history.Journal.EpochResets).IsEqualTo(1L);
      await Assert.That(history.Events).HasSingleItem();
      await Assert.That(history.Events[0].Reason)
          .IsEqualTo("scale-set-throttled");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Expired_History_Provenance_Survives_The_Longest_Retention(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("provenance");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      var retention = Retention with
      {
        SampleRetention = TimeSpan.FromHours(1),
        RollupRetention = TimeSpan.FromHours(1),
        EventRetention = TimeSpan.FromHours(1),
        DiagnosticRetention = TimeSpan.FromHours(1),
        MaximumProfilesPerNode = 1,
        GlobalSweepInterval = TimeSpan.Zero,
        MaximumQueryRange = TimeSpan.FromDays(30),
      };
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin) with { ProfileId = "vanished" }],
          Origin,
          retention,
          cancellationToken);
      await FleetStorageTestTransactions.AppendAsync(
          store,
          connectionFactory,
          nodeId,
          [CreateProfile(Origin.AddHours(6))],
          Origin.AddHours(6),
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
          Origin.AddHours(7),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var vanished = history!.Profiles.Single(
          profile => profile.ProfileId == "vanished");
      await Assert.That(vanished.Journal.Status).IsEqualTo("expired");
      await Assert.That(vanished.Retention.HistoryExpiredAt).IsNotNull();
      await Assert.That(vanished.Retention.DroppedSamples).IsEqualTo(1L);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Tied_Timestamps_Across_Profiles_Evict_Deterministically(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("tiedprofiles");
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteFleetHistoryStore(connectionFactory);
      for (var index = 0; index < 2; index++)
      {
        await FleetStorageTestTransactions.AppendAsync(
            store,
            connectionFactory,
            nodeId,
            [
                CreateProfile(Origin.AddMinutes(index)) with
                {
                  ProfileId = "alpha",
                },
                CreateProfile(Origin.AddMinutes(index)) with
                {
                  ProfileId = "beta",
                },
            ],
            Origin.AddMinutes(index),
            index == 0
                ? Retention
                : Retention with { MaximumSamplesPerNode = 3 },
            cancellationToken);
      }

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
          Origin.AddHours(1),
          cancellationToken);
      await Assert.That(history).IsNotNull();
      var alpha = history!.Profiles.Single(
          profile => profile.ProfileId == "alpha");
      var beta = history.Profiles.Single(
          profile => profile.ProfileId == "beta");
      await Assert.That(alpha.Samples.Count).IsEqualTo(2);
      await Assert.That(beta.Samples).HasSingleItem();
      await Assert.That(beta.Samples[0].ObservedAt)
          .IsEqualTo(Origin.AddMinutes(1));
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Migration_Eight_Backfills_High_Water_Without_Raw_Samples(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("migrationeight");
    try
    {
      var connectionFactory = new SqliteConnectionFactory(
          Options.Create(new SqliteFleetStoreOptions
          {
            DatabasePath = databasePath,
          }));
      await ApplyMigrationsThroughAsync(
          connectionFactory,
          7,
          cancellationToken);
      await ExecuteAsync(
          connectionFactory,
          """
          INSERT INTO tenants (tenant_id, display_name, created_at)
          VALUES ('tenant', 'Tenant', '2026-07-24T12:00:00.0000000+00:00');

          INSERT INTO nodes (
              node_id,
              tenant_id,
              connector_instance_id,
              display_name,
              credential_hash,
              connector_version,
              enrolled_at,
              last_seen_at)
          VALUES (
              'node',
              'tenant',
              'connector-instance',
              'Connector name',
              'credential-hash',
              '1.0.0',
              '2026-07-24T12:00:00.0000000+00:00',
              '2026-07-24T12:00:00.0000000+00:00');

          INSERT INTO profiles (
              node_id,
              profile_id,
              payload_hash,
              payload_json,
              observed_at)
          VALUES (
              'node',
              'projected',
              'hash',
              '{}',
              '2026-07-24T12:02:00.0000000+00:00');

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
              'node',
              'rolled',
              '2026-07-24T12:00:00.0000000+00:00',
              1,
              4,
              2,
              0,
              1,
              0,
              0);

          INSERT INTO profile_history_cursors (
              node_id,
              profile_id,
              journal_status,
              journal_capacity,
              epoch,
              epoch_resets,
              manager_highest_sequence,
              manager_dropped_events,
              stored_highest_sequence,
              missed_events,
              dropped_samples,
              dropped_rollups,
              dropped_events,
              dropped_subsystem_health,
              dropped_capacity_deficits,
              rejected_future_samples,
              rejected_future_events,
              updated_at)
          SELECT
              'node',
              profile_id,
              'current',
              64,
              0,
              0,
              NULL,
              0,
              NULL,
              0,
              0,
              0,
              0,
              0,
              0,
              0,
              0,
              '2026-07-24T12:02:00.0000000+00:00'
          FROM (SELECT 'projected' AS profile_id
                UNION ALL
                SELECT 'rolled' AS profile_id);
          """,
          cancellationToken);

      await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
          cancellationToken);

      var projected = await ReadHighWaterAsync(
          connectionFactory,
          "projected",
          cancellationToken);
      var rolled = await ReadHighWaterAsync(
          connectionFactory,
          "rolled",
          cancellationToken);
      await Assert.That(projected)
          .IsEqualTo("2026-07-24T12:02:00.0000000+00:00");
      await Assert.That(rolled)
          .IsEqualTo("2026-07-24T12:00:00.0000000+00:00");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  private static async Task ApplyMigrationsThroughAsync(
      SqliteConnectionFactory connectionFactory,
      int version,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using (var setup = connection.CreateCommand())
    {
      setup.CommandText =
          """
          CREATE TABLE IF NOT EXISTS schema_migrations (
              version INTEGER PRIMARY KEY,
              name TEXT NOT NULL,
              checksum TEXT NOT NULL,
              applied_at TEXT NOT NULL
          );
          """;
      await setup.ExecuteNonQueryAsync(cancellationToken);
    }

    foreach (var migration in SqliteMigrationCatalog.All
        .Where(candidate => candidate.Version <= version))
    {
      await using var command = connection.CreateCommand();
      command.CommandText = migration.Sql;
      await command.ExecuteNonQueryAsync(cancellationToken);
      await using var record = connection.CreateCommand();
      record.CommandText =
          """
          INSERT INTO schema_migrations (
              version,
              name,
              checksum,
              applied_at)
          VALUES ($version, $name, $checksum, $appliedAt);
          """;
      record.Parameters.AddWithValue("$version", migration.Version);
      record.Parameters.AddWithValue("$name", migration.Name);
      record.Parameters.AddWithValue("$checksum", migration.Checksum);
      record.Parameters.AddWithValue(
          "$appliedAt",
          Origin.ToString("O", CultureInfo.InvariantCulture));
      await record.ExecuteNonQueryAsync(cancellationToken);
    }
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

  private static async Task<string?> ReadHighWaterAsync(
      SqliteConnectionFactory connectionFactory,
      string profileId,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT sample_high_water
        FROM profile_history_cursors
        WHERE node_id = 'node'
          AND profile_id = $profileId;
        """;
    command.Parameters.AddWithValue("$profileId", profileId);
    return await command.ExecuteScalarAsync(cancellationToken) as string;
  }

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
    var store = new SqliteFleetStore(connectionFactory);
    await store.CreateEnrollmentCodeAsync(
        Guid.NewGuid(),
        "tenant",
        "code-hash",
        "Enrollment",
        owner.GitHubUserId,
        Origin,
        Origin.AddMinutes(10),
        cancellationToken);
    var enrollment = await store.RedeemEnrollmentCodeAsync(
        "code-hash",
        "connector-instance",
        "Connector name",
        "credential-hash",
        Origin,
        cancellationToken);
    return (
        connectionFactory,
        enrollment.NodeId ??
            throw new InvalidOperationException(
                "Enrollment did not return a node ID."));
  }
}
