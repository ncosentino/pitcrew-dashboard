using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteAlertIncidentStoreTests
{
  private static readonly DateTimeOffset Origin = new(
      2026,
      7,
      28,
      1,
      0,
      0,
      TimeSpan.Zero);

  [Test]
  public async Task Pending_Debounce_Survives_Store_Restart(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("restart");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var candidate = CreateCandidate(
          "offline",
          "tenant",
          TimeSpan.FromMinutes(2));
      await new SqliteAlertIncidentStore(factory).ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var restarted = new SqliteAlertIncidentStore(factory);
      await restarted.ReconcileAsync(
          [candidate],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var pending = await restarted.GetAsync(
          "tenant",
          AlertIncidentFilter.All,
          100,
          Origin.AddMinutes(1),
          cancellationToken);
      await Assert.That(pending.Incidents).IsEmpty();

      await restarted.ReconcileAsync(
          [candidate],
          [],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var triggered = await restarted.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken);
      await Assert.That(triggered.Incidents).HasSingleItem();
      await Assert.That(triggered.Incidents[0].Status)
          .IsEqualTo("triggered");
      await Assert.That(triggered.Incidents[0].TriggeredAt)
          .IsEqualTo(Origin.AddMinutes(2));
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Brief_Pending_Condition_Leaves_No_Incident_History(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("brief");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      await store.ReconcileAsync(
          [CreateCandidate("transient", "tenant", TimeSpan.FromMinutes(2))],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var history = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.All,
          100,
          Origin.AddMinutes(1),
          cancellationToken);
      await Assert.That(history.Incidents).IsEmpty();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Acknowledged_Incident_Resolves_Without_Losing_History_Or_Tenant_Isolation(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("acknowledge");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      await store.ReconcileAsync(
          [CreateCandidate("failure", "tenant", TimeSpan.Zero)],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var active = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken);
      await Assert.That(active.Incidents).HasSingleItem();
      var incidentId = active.Incidents[0].IncidentId;

      await Assert.That(await store.AcknowledgeAsync(
          "other",
          incidentId,
          "1",
          Origin.AddMinutes(1),
          cancellationToken))
          .IsEqualTo(AlertAcknowledgeStatus.NotFound);
      await Assert.That(await store.AcknowledgeAsync(
          "tenant",
          incidentId,
          "1",
          Origin.AddMinutes(1),
          cancellationToken))
          .IsEqualTo(AlertAcknowledgeStatus.Succeeded);
      var acknowledged = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken);
      await Assert.That(acknowledged.Incidents).HasSingleItem();
      await Assert.That(acknowledged.Incidents[0].Status)
          .IsEqualTo("acknowledged");
      await Assert.That(
          acknowledged.Incidents[0].AcknowledgedByGitHubUserId)
          .IsEqualTo("1");

      await store.ReconcileAsync(
          [],
          [],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var resolved = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Resolved,
          100,
          Origin.AddMinutes(2),
          cancellationToken);
      await Assert.That(resolved.Incidents).HasSingleItem();
      await Assert.That(resolved.Incidents[0].Status)
          .IsEqualTo("resolved");
      await Assert.That(resolved.Incidents[0].ResolvedAt)
          .IsEqualTo(Origin.AddMinutes(2));
      await Assert.That(resolved.Incidents[0].AcknowledgedAt)
          .IsEqualTo(Origin.AddMinutes(1));
      await Assert.That(
              async () => await UpdateResolvedSummaryAsync(
                  factory,
                  incidentId,
                  cancellationToken))
          .Throws<SqliteException>()
          .Because("resolved incident history is immutable");
      await store.ReconcileAsync(
          [CreateCandidate("failure", "tenant", TimeSpan.Zero)],
          [],
          Origin.AddMinutes(3),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var recurrent = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(3),
          cancellationToken);
      await Assert.That(recurrent.Incidents).HasSingleItem();
      await Assert.That(recurrent.Incidents[0].IncidentId)
          .IsNotEqualTo(incidentId);
      var otherTenant = await store.GetAsync(
          "other",
          AlertIncidentFilter.All,
          100,
          Origin.AddMinutes(2),
          cancellationToken);
      await Assert.That(otherTenant.Incidents).IsEmpty();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Resolved_History_Is_Age_And_Count_Bounded(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("retention");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      for (var index = 0; index < 4; index++)
      {
        var observedAt = Origin.AddHours(index);
        await store.ReconcileAsync(
            [
                CreateCandidate(
                    $"episode-{index}",
                    "tenant",
                    TimeSpan.Zero),
            ],
            [],
            observedAt,
            Origin.AddDays(-90),
            100,
            cancellationToken);
        await store.ReconcileAsync(
            [],
            [],
            observedAt.AddMinutes(1),
            Origin.AddDays(-90),
            100,
            cancellationToken);
      }

      await store.ReconcileAsync(
          [],
          [],
          Origin.AddHours(5),
          Origin.AddHours(1).AddSeconds(30),
          2,
          cancellationToken);
      var history = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Resolved,
          100,
          Origin.AddHours(5),
          cancellationToken);
      await Assert.That(history.Incidents.Count).IsEqualTo(2);
      await Assert.That(history.Incidents[0].Reason)
          .IsEqualTo("episode-3");
      await Assert.That(history.Incidents[1].Reason)
          .IsEqualTo("episode-2");
      await Assert.That(history.Truncated).IsFalse()
          .Because("the requested limit included every retained incident");
      var limited = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Resolved,
          1,
          Origin.AddHours(5),
          cancellationToken);
      await Assert.That(limited.Incidents).HasSingleItem();
      await Assert.That(limited.Truncated).IsTrue()
          .Because("one older retained incident was hidden by the query limit");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Unavailable_Evidence_Preserves_Triggered_And_Restarts_Pending_Debounce(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("suppression");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var triggered = CreateCandidate(
          "triggered",
          "tenant",
          TimeSpan.Zero);
      var pending = CreateCandidate(
          "pending",
          "tenant",
          TimeSpan.FromMinutes(5));
      await store.ReconcileAsync(
          [triggered, pending],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);

      await store.ReconcileAsync(
          [],
          [
              new AlertSuppression(
                  null,
                  triggered.NodeId,
                  "default",
                  null),
          ],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var active = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken);
      await Assert.That(active.Incidents).HasSingleItem();
      await Assert.That(active.Incidents[0].Reason)
          .IsEqualTo("triggered");

      await store.ReconcileAsync(
          [pending],
          [
              new AlertSuppression(
                  triggered.Key,
                  triggered.NodeId,
                  "default",
                  triggered.Kind),
          ],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var beforeBoundary = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken);
      await Assert.That(beforeBoundary.Incidents).HasSingleItem();
      await Assert.That(beforeBoundary.Incidents[0].Reason)
          .IsEqualTo("triggered");

      await store.ReconcileAsync(
          [pending],
          [
              new AlertSuppression(
                  triggered.Key,
                  triggered.NodeId,
                  "default",
                  triggered.Kind),
          ],
          Origin.AddMinutes(6),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var afterBoundary = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(6),
          cancellationToken);
      await Assert.That(afterBoundary.Incidents.Count).IsEqualTo(2);

      await store.ReconcileAsync(
          [],
          [],
          Origin.AddMinutes(7),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var resolved = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Resolved,
          100,
          Origin.AddMinutes(7),
          cancellationToken);
      await Assert.That(resolved.Incidents.Count).IsEqualTo(2);
      await Assert.That(resolved.Incidents.Count(
          incident => incident.Reason == "triggered")).IsEqualTo(1);
      await Assert.That(resolved.Incidents.Count(
          incident => incident.Reason == "pending")).IsEqualTo(1);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Unacknowledge_Returns_Acknowledged_Incident_To_Triggered(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("unacknowledge");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      await store.ReconcileAsync(
          [CreateCandidate("failure", "tenant", TimeSpan.Zero)],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var active = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken);
      await Assert.That(active.Incidents).HasSingleItem();
      var incidentId = active.Incidents[0].IncidentId;

      await store.AcknowledgeAsync(
          "tenant",
          incidentId,
          "1",
          Origin.AddMinutes(1),
          cancellationToken);

      await Assert.That(await store.UnacknowledgeAsync(
          "other",
          incidentId,
          Origin.AddMinutes(2),
          cancellationToken))
          .IsEqualTo(AlertUnacknowledgeStatus.NotFound);
      await Assert.That(await store.UnacknowledgeAsync(
          "tenant",
          incidentId,
          Origin.AddMinutes(2),
          cancellationToken))
          .IsEqualTo(AlertUnacknowledgeStatus.Succeeded);

      var afterUnack = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken);
      await Assert.That(afterUnack.Incidents).HasSingleItem();
      await Assert.That(afterUnack.Incidents[0].Status)
          .IsEqualTo("triggered");
      await Assert.That(afterUnack.Incidents[0].AcknowledgedAt)
          .IsNull();
      await Assert.That(
          afterUnack.Incidents[0].AcknowledgedByGitHubUserId)
          .IsNull();

      await Assert.That(await store.UnacknowledgeAsync(
          "tenant",
          incidentId,
          Origin.AddMinutes(2),
          cancellationToken))
          .IsEqualTo(AlertUnacknowledgeStatus.AlreadyTriggered);

      await store.ReconcileAsync(
          [],
          [],
          Origin.AddMinutes(3),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await Assert.That(await store.UnacknowledgeAsync(
          "tenant",
          incidentId,
          Origin.AddMinutes(4),
          cancellationToken))
          .IsEqualTo(AlertUnacknowledgeStatus.Resolved);

      await Assert.That(await store.UnacknowledgeAsync(
          "tenant",
          Guid.NewGuid(),
          Origin.AddMinutes(4),
          cancellationToken))
          .IsEqualTo(AlertUnacknowledgeStatus.NotFound);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  private static AlertCandidate CreateCandidate(
      string reason,
      string tenantId,
      TimeSpan debounce) =>
      new(
          $"test|{tenantId}|{reason}",
          tenantId,
          Guid.Parse(
              "11111111-1111-1111-1111-111111111111",
              CultureInfo.InvariantCulture),
          "default",
          "test-alert",
          "warning",
          Origin,
          debounce,
          "Test incident",
          "Test incident summary.",
          reason,
          null,
          $"/tenants/{tenantId}/nodes/11111111-1111-1111-1111-111111111111");

  private static async Task UpdateResolvedSummaryAsync(
      SqliteConnectionFactory factory,
      Guid incidentId,
      CancellationToken cancellationToken)
  {
    await using var connection = await factory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE alert_incidents
        SET summary = 'mutated'
        WHERE incident_id = $incidentId;
        """;
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<SqliteConnectionFactory> CreateDatabaseAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var factory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    await new SqliteMigrationRunner(factory).ApplyAsync(cancellationToken);
    var access = new SqliteAccessStore(factory);
    var owner = new DashboardUser("1", "owner", "Owner", null);
    await access.EnsureTenantOwnerAsync(
        "tenant",
        "Tenant",
        owner,
        Origin,
        cancellationToken);
    await access.EnsureTenantOwnerAsync(
        "other",
        "Other",
        owner,
        Origin,
        cancellationToken);
    return factory;
  }

  private static string CreateDatabasePath(string label) =>
      Path.Combine(
          Path.GetTempPath(),
          $"pitcrew-alerts-{label}-{Guid.NewGuid():N}.db");

  private static void Cleanup(string databasePath)
  {
    SqliteConnection.ClearAllPools();
    DashboardTestCleanup.DeleteDatabase(databasePath);
  }
}
