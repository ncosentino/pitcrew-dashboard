using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteAlertEvidenceStoreTests
{
  private static readonly DateTimeOffset Origin = new(
      2026,
      7,
      28,
      4,
      0,
      0,
      TimeSpan.Zero);

  [Test]
  public async Task Loads_Current_Profile_And_Bounded_Recent_Resource_Samples(
      CancellationToken cancellationToken)
  {
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-alert-evidence-{Guid.NewGuid():N}.db");
    try
    {
      var (factory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var history = new SqliteFleetHistoryStore(factory);
      for (var index = 0; index < 3; index++)
      {
        var observedAt = Origin.AddMinutes(index);
        await FleetStorageTestTransactions.AppendAsync(
            history,
            factory,
            nodeId,
            [CreateProfile(observedAt, index)],
            observedAt,
            CreateRetention(),
            cancellationToken);
      }
      await FleetStorageTestTransactions.ApplySyncAsync(
          new SqliteFleetStore(factory),
          factory,
          nodeId,
          "2.0.0",
          Origin.AddMinutes(2),
          [CreateProfile(Origin.AddMinutes(2), 2)],
          new ConnectorCredentialUpdate(
              ConnectorCredentialUpdateKind.None,
              string.Empty),
          cancellationToken);

      var snapshot = await new SqliteAlertEvidenceStore(factory).LoadAsync(
          Origin.AddHours(-1),
          2,
          cancellationToken);

      await Assert.That(snapshot.Nodes).HasSingleItem();
      var node = snapshot.Nodes[0];
      await Assert.That(node.TenantId).IsEqualTo("tenant");
      await Assert.That(node.Profiles).HasSingleItem();
      var profile = node.Profiles[0];
      await Assert.That(profile.Observation.ObservedAt)
          .IsEqualTo(Origin.AddMinutes(2));
      await Assert.That(profile.RecentResourceSamples.Count).IsEqualTo(2);
      await Assert.That(profile.RecentResourceSamples[0].ObservedAt)
          .IsEqualTo(Origin.AddMinutes(1));
      await Assert.That(profile.RecentResourceSamples[1].ObservedAt)
          .IsEqualTo(Origin.AddMinutes(2));
      await Assert.That(profile.RecentResourceSamples[1].CpuCores)
          .IsEqualTo(3.5);
      await Assert.That(profile.RecentResourceSamples[1].MemoryBytes)
          .IsEqualTo(3000L);
      await Assert.That(profile.Journal.Status).IsEqualTo("unreported");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static ManagerObservedState CreateProfile(
      DateTimeOffset observedAt,
      int index) =>
      new(
          1,
          11,
          "default",
          "manager",
          "running",
          observedAt,
          "repository",
          1,
          new string('a', 64),
          "accepted",
          1,
          1,
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
                      2.5,
                      2000,
                      20,
                      index * 1000,
                      index * 1000,
                      index * 500,
                      index * 500),
                  "idle",
                  null,
                  "connected",
                  $"sha256:{new string('b', 64)}"),
          ],
          new ManagerResourceTelemetry(
              observedAt,
              "available",
              new HostResourceCapacity(4, 10_000),
              new ResourceUsage(1, 1000, 10)),
          1,
          null);

  private static HistoryRetentionPolicy CreateRetention() =>
      new(
          TimeSpan.FromDays(7),
          TimeSpan.FromDays(90),
          TimeSpan.FromDays(30),
          TimeSpan.FromDays(30),
          1000,
          1000,
          1000,
          1000,
          1000,
          1000,
          1000,
          100,
          10_000,
          10_000,
          10_000,
          10_000,
          1000,
          100,
          TimeSpan.FromMinutes(5),
          TimeSpan.FromDays(90));

  private static async Task<(
      SqliteConnectionFactory Factory,
      Guid NodeId)> CreateEnrolledNodeAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var factory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    await new SqliteMigrationRunner(factory).ApplyAsync(cancellationToken);
    await new SqliteAccessStore(factory).EnsureTenantOwnerAsync(
        "tenant",
        "Tenant",
        new DashboardUser("1", "owner", "Owner", null),
        Origin,
        cancellationToken);
    var store = new SqliteFleetStore(factory);
    await store.CreateEnrollmentCodeAsync(
        Guid.NewGuid(),
        "tenant",
        "code-hash",
        "Enrollment",
        "1",
        Origin,
        Origin.AddMinutes(10),
        cancellationToken);
    var enrollment = await store.RedeemEnrollmentCodeAsync(
        "code-hash",
        "connector",
        "Connector",
        "credential-hash",
        Origin,
        cancellationToken);
    return (
        factory,
        enrollment.NodeId ??
            throw new InvalidOperationException(
                "Enrollment did not return a node identifier."));
  }
}
