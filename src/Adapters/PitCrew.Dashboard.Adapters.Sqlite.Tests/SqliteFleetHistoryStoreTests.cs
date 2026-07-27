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

  private static readonly HistoryRetentionPolicy Retention = new(
      TimeSpan.FromDays(14),
      TimeSpan.FromDays(90),
      TimeSpan.FromDays(30),
      100_000,
      20_000);

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

      await store.AppendAsync(
          nodeId,
          [profile],
          Origin,
          Retention,
          cancellationToken);
      await store.AppendAsync(
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
        await store.AppendAsync(
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
      await store.AppendAsync(
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
      await store.AppendAsync(
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
      await store.AppendAsync(
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
      var retention = new HistoryRetentionPolicy(
          TimeSpan.FromMinutes(30),
          TimeSpan.FromMinutes(90),
          TimeSpan.FromMinutes(30),
          2,
          2);
      for (var index = 0; index < 5; index++)
      {
        await store.AppendAsync(
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

      await store.AppendAsync(
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
        await store.AppendAsync(
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
      var baseline = await MeasureDatabaseBytesAsync(
          connectionFactory,
          cancellationToken);
      for (var index = 0; index < samples; index++)
      {
        var observedAt = Origin.AddSeconds(15 * index);
        await store.AppendAsync(
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
      }

      var measured = await MeasureDatabaseBytesAsync(
          connectionFactory,
          cancellationToken);
      var growth = measured - baseline;
      var bytesPerSample = growth / (double)samples;
      var measurement = string.Create(
          CultureInfo.InvariantCulture,
          $"Measured history growth: {growth} bytes for {samples} samples ({bytesPerSample:F1} bytes per sample).");
      if (TestContext.Current is { } testContext)
      {
        await testContext.OutputWriter.WriteLineAsync(measurement);
      }
      await Assert.That(bytesPerSample).IsLessThan(400d);

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

  private static async Task<long> MeasureDatabaseBytesAsync(
      SqliteConnectionFactory connectionFactory,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA page_count;";
    var pages = Convert.ToInt64(
        await command.ExecuteScalarAsync(cancellationToken),
        CultureInfo.InvariantCulture);
    await using var pageSizeCommand = connection.CreateCommand();
    pageSizeCommand.CommandText = "PRAGMA page_size;";
    var pageSize = Convert.ToInt64(
        await pageSizeCommand.ExecuteScalarAsync(cancellationToken),
        CultureInfo.InvariantCulture);
    return pages * pageSize;
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
          eventLimit);

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
