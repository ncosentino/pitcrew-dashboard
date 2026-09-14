using System.Globalization;

using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Trust.Scenarios;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed partial class SqliteAlertIncidentStoreTests
{
  [Test]
  public async Task Missing_Evidence_Does_Not_Resolve_A_Triggered_Condition(
      CancellationToken cancellationToken)
  {
    var scenario = FleetTrustScenarioCorpus.Load()
        .FleetEvidence.Single(
            candidate => candidate.Id == "reporting-loss-retained-evidence");
    var databasePath = CreateDatabasePath("missing-evidence");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      await store.ReconcileAsync(
          [CreateCandidate("retained-condition", "tenant", TimeSpan.Zero)],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);

      await store.ReconcileAsync(
          [],
          [],
          scenario.Clocks.EvaluatedAt,
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var active = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          scenario.Clocks.ResponseGeneratedAt,
          cancellationToken);
      await Assert.That(scenario.TrueResolutionProven).IsFalse()
          .Because("the corpus marks retained evidence as insufficient to prove recovery");
      await Assert.That(active.Incidents).HasSingleItem();
      await Assert.That(active.Incidents[0].ResolvedAt).IsNull();
      await Assert.That(ReadProperty<string>(
          active.Incidents[0],
          "ConditionState"))
          .IsEqualTo("waiting-for-evidence");
      await Assert.That(ReadProperty<string?>(
          active.Incidents[0],
          "CurrentSeverity"))
          .IsNull();
      await Assert.That(ReadProperty<string>(
          active.Incidents[0],
          "LastConfirmedSeverity"))
          .IsEqualTo("warning");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Disconnect_Suppression_Preserves_Node_Scoped_Pressure(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("node-suppression");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateCandidate(
          "host-pressure",
          "tenant",
          TimeSpan.Zero) with
      {
        ProfileId = null,
      };
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);

      await store.ReconcileAsync(
          [],
          [new AlertSuppression(null, candidate.NodeId, null, null)],
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
          .IsEqualTo("host-pressure");
      await Assert.That(ReadProperty<string>(
          active.Incidents[0],
          "ConditionState"))
          .IsEqualTo("waiting-for-evidence");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Waiting_State_Survives_Restart_And_Fresh_Truth_Resumes_Confirmation(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("waiting-restart");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var candidate = CreateCandidate(
          "restart-waiting",
          "tenant",
          TimeSpan.Zero);
      var store = new SqliteAlertIncidentStore(factory);
      await store.ReconcileAsync(
          [candidate],
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

      var restarted = new SqliteAlertIncidentStore(factory);
      var waiting = await restarted.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken);
      await Assert.That(ReadProperty<string>(
          waiting.Incidents.Single(),
          "ConditionState"))
          .IsEqualTo("waiting-for-evidence");

      await restarted.ReconcileAsync(
          [candidate],
          [],
          Origin.AddMinutes(1).AddSeconds(30),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var replayed = await restarted.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1).AddSeconds(30),
          cancellationToken);
      await Assert.That(replayed.Incidents.Single().ConditionState)
          .IsEqualTo("waiting-for-evidence");
      await Assert.That(replayed.Incidents.Single().CurrentSeverity)
          .IsNull();

      await restarted.ReconcileAsync(
          [candidate with
          {
            SourceObservedAt = Origin.AddMinutes(2),
            DashboardReceivedAt = Origin.AddMinutes(2),
          }],
          [],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var confirmed = await restarted.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken);
      await Assert.That(ReadProperty<string>(
          confirmed.Incidents.Single(),
          "ConditionState"))
          .IsEqualTo("confirmed");
      await Assert.That(ReadProperty<string?>(
          confirmed.Incidents.Single(),
          "CurrentSeverity"))
          .IsEqualTo("warning");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Pre_Clearance_Replay_Cannot_Create_A_Recurrence(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("stale-recurrence");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateCandidate(
          "recurring-condition",
          "tenant",
          TimeSpan.Zero);
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [],
          [new AlertClearance(
              candidate.Key,
              Origin.AddMinutes(1),
              Origin.AddMinutes(1))],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);

      await store.ReconcileAsync(
          [candidate],
          [],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var staleReplay = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.All,
          100,
          Origin.AddMinutes(2),
          cancellationToken);
      await Assert.That(staleReplay.Incidents).HasSingleItem();
      await Assert.That(staleReplay.Incidents.Single().Status)
          .IsEqualTo("resolved");

      await store.ReconcileAsync(
          [candidate with
          {
            FirstObservedAt = Origin.AddMinutes(3),
            SourceObservedAt = Origin.AddMinutes(3),
            DashboardReceivedAt = Origin.AddMinutes(3),
          }],
          [],
          Origin.AddMinutes(3),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var recurrence = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.All,
          100,
          Origin.AddMinutes(3),
          cancellationToken);
      await Assert.That(recurrence.Incidents).Count().IsEqualTo(2);
      await Assert.That(recurrence.Incidents.Count(
          incident => incident.Status == "triggered"))
          .IsEqualTo(1);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Replayed_And_Stale_Evidence_Cannot_Clear_An_Active_Incident(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("stale-clearance");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateCandidate(
          "replayed-condition",
          "tenant",
          TimeSpan.Zero) with
      {
        SourceObservedAt = Origin,
        DashboardReceivedAt = Origin,
      };
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);

      await store.ReconcileAsync(
          [candidate with
          {
            Severity = "critical",
            SourceObservedAt = Origin.AddMinutes(-1),
            DashboardReceivedAt = Origin.AddMinutes(1),
          }],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [],
          [new AlertClearance(
              candidate.Key,
              Origin,
              Origin.AddMinutes(2))],
          [],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var stillActive = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken);
      await Assert.That(stillActive.Incidents).HasSingleItem();
      await Assert.That(stillActive.Incidents[0].Severity)
          .IsEqualTo("warning")
          .Because("a stale replay cannot replace newer condition evidence");
      await Assert.That(stillActive.Incidents[0].SourceObservedAt)
          .IsEqualTo(Origin);
      await Assert.That(stillActive.Incidents[0].DashboardReceivedAt)
          .IsEqualTo(Origin);
      await Assert.That(stillActive.Incidents[0].EvaluatedAt)
          .IsEqualTo(Origin.AddMinutes(2));
      await Assert.That(stillActive.Incidents[0].ConditionState)
          .IsEqualTo("waiting-for-evidence");
      await Assert.That(stillActive.Incidents[0].CurrentSeverity)
          .IsNull();
      await Assert.That(stillActive.GeneratedAt)
          .IsEqualTo(Origin.AddMinutes(2));

      await store.ReconcileAsync(
          [],
          [new AlertClearance(
              candidate.Key,
              Origin.AddMinutes(3),
              Origin.AddMinutes(3))],
          [],
          Origin.AddMinutes(3),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var resolved = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Resolved,
          100,
          Origin.AddMinutes(3),
          cancellationToken);
      await Assert.That(resolved.Incidents).HasSingleItem();
      await Assert.That(resolved.Incidents[0].ResolutionEvidence)
          .IsEqualTo("fresh");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Revoked_Observation_Contract_Projects_Monitoring_Ended(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("monitoring-ended");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateCandidate(
          "revoked-condition",
          "tenant",
          TimeSpan.Zero);
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);

      await store.ReconcileAsync(
          [],
          [],
          [new AlertSuppression(
              null,
              candidate.NodeId,
              null,
              null,
              "monitoring-ended")],
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
      await Assert.That(active.Incidents[0].ConditionState)
          .IsEqualTo("monitoring-ended");
      await Assert.That(active.Incidents[0].CurrentSeverity)
          .IsNull();
      await Assert.That(active.Incidents[0].LastConfirmedSeverity)
          .IsEqualTo("warning");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Acknowledged_Material_Escalation_Creates_One_Unowned_Revision(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("acknowledged-escalation");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateCandidate(
          "acknowledged-escalation",
          "tenant",
          TimeSpan.Zero);
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var incidentId = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken)).Incidents.Single().IncidentId;
      await store.AcknowledgeAsync(
          "tenant",
          incidentId,
          "1",
          Origin.AddSeconds(1),
          cancellationToken);
      var escalated = candidate with
      {
        Severity = "critical",
        SourceObservedAt = Origin.AddMinutes(1),
        DashboardReceivedAt = Origin.AddMinutes(1),
      };

      await Task.WhenAll(
          store.ReconcileAsync(
              [escalated],
              [],
              Origin.AddMinutes(1),
              Origin.AddDays(-90),
              100,
              cancellationToken),
          new SqliteAlertIncidentStore(factory).ReconcileAsync(
              [escalated],
              [],
              Origin.AddMinutes(1),
              Origin.AddDays(-90),
              100,
              cancellationToken));

      var current = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken)).Incidents.Single();
      await Assert.That(current.Status).IsEqualTo("triggered");
      await Assert.That(current.AcknowledgedAt).IsNull();
      await Assert.That(current.AcknowledgedByGitHubUserId).IsNull();
      await Assert.That(ReadProperty<string>(current, "OperatorState"))
          .IsEqualTo("unowned");
      await Assert.That(ReadProperty<int>(current, "Revision"))
          .IsEqualTo(2);
      await Assert.That(ReadProperty<string>(current, "PeakSeverity"))
          .IsEqualTo("critical");

      await using var connection = await factory.OpenAsync(cancellationToken);
      await using var command = connection.CreateCommand();
      command.CommandText =
          """
          SELECT COUNT(*)
          FROM alert_incident_acknowledgement_events
          WHERE tenant_id = 'tenant'
            AND incident_id = $incidentId
            AND action = 'acknowledged';
          """;
      command.Parameters.AddWithValue("$incidentId", incidentId.ToString("D"));
      await Assert.That(Convert.ToInt32(
          await command.ExecuteScalarAsync(cancellationToken),
          CultureInfo.InvariantCulture))
          .IsEqualTo(1);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Attention_Order_Surfaces_Older_Critical_Before_Two_Hundred_Warnings(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("attention-order");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var warnings = Enumerable.Range(0, 201)
          .Select(index => CreateCandidate(
              $"warning-{index:D3}",
              "tenant",
              TimeSpan.Zero) with
          {
            SourceObservedAt = Origin.AddMinutes(index + 1),
            DashboardReceivedAt = Origin.AddMinutes(index + 1),
          });
      var critical = CreateCandidate(
          "older-critical",
          "tenant",
          TimeSpan.Zero) with
      {
        Severity = "critical",
        SourceObservedAt = Origin.AddMinutes(-1),
        DashboardReceivedAt = Origin,
      };
      await store.ReconcileAsync(
          [.. warnings, critical],
          [],
          Origin.AddHours(4),
          Origin.AddDays(-90),
          10_000,
          cancellationToken);

      var seen = new HashSet<Guid>();
      AlertIncidentCursor? cursor = null;
      AlertIncidentPage page;
      var pageIndex = 0;
      do
      {
        page = await store.GetPageAsync(
            "tenant",
            AlertIncidentFilter.Active,
            200,
            cursor,
            Origin.AddHours(4),
            cancellationToken);
        if (pageIndex == 0)
        {
          await Assert.That(page.Incidents[0].IncidentId)
              .IsEqualTo((await store.GetAsync(
                  "tenant",
                  AlertIncidentFilter.Active,
                  300,
                  Origin.AddHours(4),
                  cancellationToken)).Incidents.Single(
                      incident => incident.Reason == "older-critical").IncidentId);
          await Assert.That(page.CriticalCount).IsEqualTo(1);
        }
        foreach (var incident in page.Incidents)
        {
          await Assert.That(seen.Add(incident.IncidentId)).IsTrue();
        }
        cursor = AlertIncidentCursor.ParseOrNull(page.NextCursor);
        pageIndex++;
      }
      while (cursor is not null);

      await Assert.That(seen.Count).IsEqualTo(202);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Fresh_Escalation_Updates_The_Existing_Incident(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("fresh-escalation");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateCandidate(
          "escalating-condition",
          "tenant",
          TimeSpan.Zero);
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var original = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken);

      await store.ReconcileAsync(
          [candidate with
          {
            Severity = "critical",
            SourceObservedAt = Origin.AddMinutes(1),
            DashboardReceivedAt = Origin.AddMinutes(1),
          }],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var escalated = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken);

      await Assert.That(escalated.Incidents).HasSingleItem();
      await Assert.That(escalated.Incidents[0].IncidentId)
          .IsEqualTo(original.Incidents.Single().IncidentId);
      await Assert.That(escalated.Incidents[0].Severity)
          .IsEqualTo("critical");
      await Assert.That(escalated.Incidents[0].SourceObservedAt)
          .IsEqualTo(Origin.AddMinutes(1));
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Corpus_Page_Exposes_Authoritative_Total_And_Exact_Read(
      CancellationToken cancellationToken)
  {
    var scenario = FleetTrustScenarioCorpus.Load()
        .IncidentCardinalities.Single(
            candidate => candidate.Id == "incidents-over-200");
    var databasePath = CreateDatabasePath("authoritative-page");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidates = FleetTrustScenarioCorpus.CreateIncidents(scenario)
          .Select(incident => CreateCandidate(
              incident.ConditionKey,
              "tenant",
              TimeSpan.Zero))
          .ToArray();
      await store.ReconcileAsync(
          candidates,
          [],
          Origin,
          Origin.AddDays(-90),
          10_000,
          cancellationToken);

      var page = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          scenario.QueryLimit,
          Origin,
          cancellationToken);
      var total = page.GetType().GetProperty("TotalCount")?.GetValue(page);
      var exactMethod = typeof(IAlertIncidentStore).GetMethod("GetByIdAsync");
      var cursor = AlertIncidentCursor.ParseOrNull(page.NextCursor);
      var nextPage = await store.GetPageAsync(
          "tenant",
          AlertIncidentFilter.Active,
          scenario.QueryLimit,
          cursor,
          Origin,
          cancellationToken);
      var omittedIncident = nextPage.Incidents.Single();
      var exact = await store.GetByIdAsync(
          "tenant",
          omittedIncident.IncidentId,
          cancellationToken);
      var foreign = await store.GetByIdAsync(
          "other",
          omittedIncident.IncidentId,
          cancellationToken);

      await Assert.That(total).IsEqualTo(scenario.IncidentCount);
      await Assert.That(page.Truncated).IsTrue()
          .Because("the authoritative total exceeds the first page");
      await Assert.That(exactMethod).IsNotNull()
          .Because("deep links require tenant-scoped retrieval outside the first page");
      await Assert.That(nextPage.Incidents).HasSingleItem();
      await Assert.That(exact?.IncidentId)
          .IsEqualTo(omittedIncident.IncidentId);
      await Assert.That(foreign).IsNull();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Acknowledge_And_Undo_Leave_An_Immutable_Audit_Trail(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("acknowledgement-audit");
    try
    {
      var factory = await CreateDatabaseAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      await store.ReconcileAsync(
          [CreateCandidate("audited", "tenant", TimeSpan.Zero)],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var page = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken);
      var incidentId = page.Incidents.Single().IncidentId;

      await store.AcknowledgeAsync(
          "tenant",
          incidentId,
          "1",
          Origin.AddMinutes(1),
          cancellationToken);
      await store.UnacknowledgeAsync(
          "tenant",
          incidentId,
          "1",
          Origin.AddMinutes(2),
          cancellationToken);

      await using var connection = await factory.OpenAsync(cancellationToken);
      await using var command = connection.CreateCommand();
      command.CommandText =
          """
          SELECT action, actor_github_user_id, occurred_at
          FROM alert_incident_acknowledgement_events
          WHERE tenant_id = $tenantId
            AND incident_id = $incidentId
          ORDER BY occurred_at, event_id;
          """;
      command.Parameters.AddWithValue("$tenantId", "tenant");
      command.Parameters.AddWithValue(
          "$incidentId",
          incidentId.ToString("D"));
      await using var reader = await command.ExecuteReaderAsync(
          cancellationToken);
      var actions = new List<string>();
      while (await reader.ReadAsync(cancellationToken))
      {
        actions.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}"));
      }

      await Assert.That(actions.Count).IsEqualTo(2);
      await Assert.That(actions[0]).StartsWith("acknowledged|1|");
      await Assert.That(actions[1]).StartsWith("unacknowledged|1|");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Incident_Contract_Distinguishes_All_Four_Clocks()
  {
    var properties = typeof(AlertIncident)
        .GetProperties()
        .Select(property => property.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    await Assert.That(properties).Contains("SourceObservedAt");
    await Assert.That(properties).Contains("DashboardReceivedAt");
    await Assert.That(properties).Contains("EvaluatedAt");
    await Assert.That(typeof(AlertIncidentPage).GetProperty("GeneratedAt"))
        .IsNotNull();
  }

  private static T ReadProperty<T>(
      AlertIncident incident,
      string propertyName)
  {
    var property = incident.GetType().GetProperty(propertyName);
    if (property is null)
    {
      throw new InvalidOperationException(
          $"AlertIncident.{propertyName} is required.");
    }
    return (T)property.GetValue(incident)!;
  }
}
