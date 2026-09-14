using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Trust.Scenarios;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed partial class SqliteAlertIncidentStoreTests
{
  [Test]
  public async Task Repeated_Corpus_Signals_Produce_One_Condition_And_One_Incident(
      CancellationToken cancellationToken)
  {
    var scenario = FleetTrustScenarioCorpus.Load()
        .ConditionDecomposition.Single(
            candidate => candidate.Id == "repeated-persistent-condition");
    var databasePath = CreateDatabasePath("projection-repeated");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      foreach (var signal in scenario.Signals)
      {
        if (signal.Truth == "true")
        {
          await store.ReconcileAsync(
              [CreateProjectionCandidate(signal.ConditionKey, signal.ObservedAt)],
              [],
              signal.EvaluatedAt,
              Origin.AddDays(-90),
              100,
              cancellationToken);
        }
        else
        {
          await store.ReconcileAsync(
              [],
              [new AlertSuppression(
                  signal.ConditionKey,
                  Guid.Parse(
                      "11111111-1111-1111-1111-111111111111",
                      System.Globalization.CultureInfo.InvariantCulture),
                  "default",
                  "test-alert")],
              signal.EvaluatedAt,
              Origin.AddDays(-90),
              100,
              cancellationToken);
        }
      }

      var page = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          scenario.Signals[^1].EvaluatedAt,
          cancellationToken);

      await Assert.That(page.Incidents.Count)
          .IsEqualTo(scenario.ExpectedIncidentCount);
      await Assert.That(page.Incidents.Single().ConditionCount)
          .IsEqualTo(scenario.ExpectedConditionCount);
      await Assert.That(page.Incidents.Single().EpisodeOrdinal).IsEqualTo(1);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Compatible_Conditions_Group_Identically_In_Either_Arrival_Order(
      CancellationToken cancellationToken)
  {
    var first = CreateProjectionCandidate("condition-a", Origin);
    var second = CreateProjectionCandidate("condition-b", Origin) with
    {
      Key = "condition-b",
      Reason = "condition-b",
      Title = "Second condition",
      Summary = "Second condition summary.",
    };
    var forward = await ProjectAsync(
        "projection-forward",
        [first, second],
        cancellationToken);
    var reverse = await ProjectAsync(
        "projection-reverse",
        [second, first],
        cancellationToken);

    await Assert.That(forward.IncidentId).IsEqualTo(reverse.IncidentId);
    await Assert.That(forward.SeriesId).IsEqualTo(reverse.SeriesId);
    await Assert.That(forward.ConditionCount).IsEqualTo(2);
    await Assert.That(forward.GroupingReasons)
        .IsEquivalentTo(reverse.GroupingReasons);
    await Assert.That(forward.Title).IsEqualTo(reverse.Title);
    await Assert.That(forward.Summary).IsEqualTo(reverse.Summary);
    await Assert.That(forward.Reason).IsEqualTo(reverse.Reason);
    await Assert.That(forward.Link).IsEqualTo(reverse.Link);
  }

  [Test]
  public async Task Independent_Conditions_Remain_Separate(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-independent");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      await store.ReconcileAsync(
          [
              CreateProjectionCandidate("condition-a", Origin),
              CreateProjectionCandidate("condition-b", Origin) with
              {
                Key = "condition-b",
                Reason = "condition-b",
                InvestigationClass = "worker-recovery",
                EvidenceDependency = "manager",
              },
          ],
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

      await Assert.That(page.Incidents.Count).IsEqualTo(2);
      await Assert.That(page.Incidents.All(
          incident => incident.ConditionCount == 1)).IsTrue()
          .Because("incompatible investigations must not be collapsed");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task More_Than_Two_Hundred_Compatible_Conditions_Remain_One_Incident(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-201-conditions");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidates = Enumerable.Range(0, 201)
          .Select(index => CreateProjectionCandidate(
              $"condition-{index:D3}",
              Origin))
          .ToArray();
      await store.ReconcileAsync(
          candidates,
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var page = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          200,
          Origin,
          cancellationToken);
      await Assert.That(page.Incidents).HasSingleItem();
      await Assert.That(page.Incidents.Single().ConditionCount).IsEqualTo(201);
      await Assert.That(page.TotalCount).IsEqualTo(1);
      await Assert.That(page.Truncated).IsFalse();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Proven_Resolution_Allocates_A_Linked_Recurrence_Ordinal(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-recurrence");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateProjectionCandidate("recurring", Origin);
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var first = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken)).Incidents.Single();
      await store.ReconcileAsync(
          [],
          [CreateClearance(candidate, Origin.AddMinutes(1))],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [candidate with
          {
            FirstObservedAt = Origin.AddMinutes(2),
            SourceObservedAt = Origin.AddMinutes(2),
            DashboardReceivedAt = Origin.AddMinutes(2),
          }],
          [],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var recurrent = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken)).Incidents.Single();

      await Assert.That(recurrent.SeriesId).IsEqualTo(first.SeriesId);
      await Assert.That(recurrent.EpisodeOrdinal).IsEqualTo(2);
      await Assert.That(recurrent.PreviousIncidentId)
          .IsEqualTo(first.IncidentId);
      await Assert.That(recurrent.Transition).IsEqualTo("recurrence");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Unknown_Resets_Recovery_Hysteresis(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-hysteresis");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateProjectionCandidate("recovering", Origin);
      var clearance = CreateClearance(candidate, Origin.AddMinutes(1)) with
      {
        RequiredSamples = 2,
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
          [clearance],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [],
          [new AlertSuppression(
              candidate.Key,
              candidate.NodeId,
              candidate.ProfileId,
              candidate.Kind)],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [],
          [clearance with
          {
            SourceObservedAt = Origin.AddMinutes(3),
            DashboardReceivedAt = Origin.AddMinutes(3),
          }],
          [],
          Origin.AddMinutes(3),
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var incident = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(3),
          cancellationToken)).Incidents.Single();
      await Assert.That(incident.ConditionState).IsEqualTo("recovering");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Replayed_Clearance_Does_Not_Advance_Recovery(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-clearance-replay");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateProjectionCandidate("replayed-clearance", Origin);
      var clearance = CreateClearance(candidate, Origin.AddMinutes(1)) with
      {
        RequiredSamples = 2,
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
          [clearance],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [],
          [clearance],
          [],
          Origin.AddMinutes(10),
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var incident = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(10),
          cancellationToken)).Incidents.Single();
      await Assert.That(incident.ConditionState).IsEqualTo("recovering");
      await Assert.That(incident.ResolvedAt).IsNull();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Reporting_Loss_Grouping_Is_Tenant_Scoped(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-tenant-grouping");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var connector = CreateProjectionCandidate("connector", Origin) with
      {
        Kind = "connector-offline",
        ProfileId = null,
      };
      var other = CreateProjectionCandidate("dependent", Origin) with
      {
        Key = "other-dependent",
        TenantId = "other",
        Reason = "dependent",
        Link = "/tenants/other/nodes/11111111-1111-1111-1111-111111111111",
      };
      await store.ReconcileAsync(
          [connector, other],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [connector with
          {
            SourceObservedAt = Origin.AddMinutes(1),
            DashboardReceivedAt = Origin.AddMinutes(1),
          }],
          [new AlertSuppression(
              other.Key,
              other.NodeId,
              other.ProfileId,
              other.Kind)],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var incident = (await store.GetAsync(
          "other",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken)).Incidents.Single();
      await Assert.That(incident.GroupingReasons.Contains(
          "same-reporting-boundary",
          StringComparer.Ordinal)).IsFalse();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Conditions_Regroup_When_Evidence_Dependency_Changes(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-regroup");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var connector = CreateProjectionCandidate("connector", Origin) with
      {
        Kind = "connector-offline",
        ProfileId = null,
      };
      var dependent = CreateProjectionCandidate("dependent", Origin);
      var otherDependent = dependent with
      {
        Key = "other-dependent",
        TenantId = "other",
        Reason = "other-dependent",
        Link = "/tenants/other/nodes/11111111-1111-1111-1111-111111111111",
      };
      await store.ReconcileAsync(
          [connector, dependent, otherDependent],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var initial = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken);
      var initialDependent = initial.Incidents.Single(incident =>
          incident.Reason == dependent.Reason);
      var initialOther = (await store.GetAsync(
          "other",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken)).Incidents.Single();
      await store.ReconcileAsync(
          [connector with
          {
            SourceObservedAt = Origin.AddMinutes(1),
            DashboardReceivedAt = Origin.AddMinutes(1),
          }, otherDependent with
          {
            SourceObservedAt = Origin.AddMinutes(1),
            DashboardReceivedAt = Origin.AddMinutes(1),
          }],
          [new AlertSuppression(
              dependent.Key,
              dependent.NodeId,
              dependent.ProfileId,
              dependent.Kind)],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var grouped = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken);
      await Assert.That(grouped.Incidents).HasSingleItem();
      await Assert.That(grouped.Incidents.Single().ConditionCount).IsEqualTo(2);

      var returnedCandidates = new[]
      {
        connector with
        {
          SourceObservedAt = Origin.AddMinutes(2),
          DashboardReceivedAt = Origin.AddMinutes(2),
        },
        dependent with
        {
          SourceObservedAt = Origin.AddMinutes(2),
          DashboardReceivedAt = Origin.AddMinutes(2),
        },
        otherDependent with
        {
          SourceObservedAt = Origin.AddMinutes(2),
          DashboardReceivedAt = Origin.AddMinutes(2),
        },
      };
      await Task.WhenAll(
          new SqliteAlertIncidentStore(factory).ReconcileAsync(
              returnedCandidates,
              [],
              Origin.AddMinutes(2),
              Origin.AddDays(-90),
              100,
              cancellationToken),
          new SqliteAlertIncidentStore(factory).ReconcileAsync(
              returnedCandidates,
              [],
              Origin.AddMinutes(2),
              Origin.AddDays(-90),
              100,
              cancellationToken));
      var separated = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken);
      await Assert.That(separated.Incidents.Count).IsEqualTo(2);
      await Assert.That(separated.Incidents.All(
          incident => incident.ConditionCount == 1)).IsTrue();
      var returnedDependent = separated.Incidents.Single(incident =>
          incident.Reason == dependent.Reason);
      await Assert.That(returnedDependent.IncidentId)
          .IsEqualTo(initialDependent.IncidentId);
      await Assert.That(returnedDependent.EpisodeOrdinal).IsEqualTo(1);
      await Assert.That(returnedDependent.PreviousIncidentId).IsNull();
      await Assert.That(returnedDependent.PreviousHistoryState).IsNull();
      await Assert.That(returnedDependent.Transition).IsNull();
      var returnedOther = (await store.GetAsync(
          "other",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken)).Incidents.Single();
      await Assert.That(returnedOther.IncidentId)
          .IsEqualTo(initialOther.IncidentId);
      await Assert.That(returnedOther.EpisodeOrdinal).IsEqualTo(1);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Condition_Expansion_Returns_Acknowledged_Incident_To_Unowned(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-expansion");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var first = CreateProjectionCandidate("first", Origin);
      await store.ReconcileAsync(
          [first],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var original = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken)).Incidents.Single();
      await Assert.That(await store.AcknowledgeAsync(
          "tenant",
          original.IncidentId,
          "42",
          Origin.AddSeconds(1),
          cancellationToken)).IsEqualTo(AlertAcknowledgeStatus.Succeeded);

      await store.ReconcileAsync(
          [
              first with
              {
                SourceObservedAt = Origin.AddMinutes(1),
                DashboardReceivedAt = Origin.AddMinutes(1),
              },
              CreateProjectionCandidate("second", Origin.AddMinutes(1)),
          ],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var expanded = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken)).Incidents.Single();
      await Assert.That(expanded.ConditionCount).IsEqualTo(2);
      await Assert.That(expanded.OperatorState).IsEqualTo("unowned");
      await Assert.That(expanded.AcknowledgedAt).IsNull();
      await Assert.That(expanded.Revision).IsGreaterThan(original.Revision);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Waiting_Member_Takes_Precedence_Over_Recovering_Member(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-mixed-state");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var recovering = CreateProjectionCandidate("recovering-member", Origin);
      var waiting = CreateProjectionCandidate("waiting-member", Origin);
      await store.ReconcileAsync(
          [recovering, waiting],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [],
          [CreateClearance(recovering, Origin.AddMinutes(1)) with
          {
            RequiredSamples = 2,
          }],
          [new AlertSuppression(
              waiting.Key,
              waiting.NodeId,
              waiting.ProfileId,
              waiting.Kind)],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);

      var incident = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken)).Incidents.Single();
      await Assert.That(incident.ConditionState)
          .IsEqualTo("waiting-for-evidence");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Concurrent_Open_Attempts_Converge_On_One_Episode(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-concurrency");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var candidate = CreateProjectionCandidate("concurrent", Origin);
      await Task.WhenAll(
          new SqliteAlertIncidentStore(factory).ReconcileAsync(
              [candidate],
              [],
              Origin,
              Origin.AddDays(-90),
              100,
              cancellationToken),
          new SqliteAlertIncidentStore(factory).ReconcileAsync(
              [candidate],
              [],
              Origin,
              Origin.AddDays(-90),
              100,
              cancellationToken));

      var page = await new SqliteAlertIncidentStore(factory).GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken);
      await Assert.That(page.Incidents).HasSingleItem();
      await Assert.That(page.TotalCount).IsEqualTo(1);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Suppression_Does_Not_Change_Truth_And_Escalation_Bypasses_It(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-suppression");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateProjectionCandidate("suppressed", Origin);
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var incident = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken)).Incidents.Single();
      await Assert.That(await store.SetSuppressionAsync(
          "tenant",
          incident.IncidentId,
          "maintenance",
          Origin.AddHours(1),
          Origin,
          cancellationToken)).IsTrue()
          .Because("the active incident exists");

      var suppressed = await store.GetByIdAsync(
          "tenant",
          incident.IncidentId,
          cancellationToken);
      await Assert.That(suppressed).IsNotNull();
      await Assert.That(suppressed!.ConditionState).IsEqualTo("confirmed");
      await Assert.That(suppressed.SuppressionReason)
          .IsEqualTo("maintenance");

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
      var escalated = await store.GetByIdAsync(
          "tenant",
          incident.IncidentId,
          cancellationToken);
      await Assert.That(escalated).IsNotNull();
      await Assert.That(escalated!.CurrentSeverity).IsEqualTo("critical");
      await Assert.That(escalated.OperatorState).IsEqualTo("unowned");
      await Assert.That(escalated.SuppressionReason).IsNull();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Expired_Suppression_Returns_Incident_To_Unowned(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-suppression-expiry");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateProjectionCandidate("suppression-expiry", Origin);
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var incident = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken)).Incidents.Single();
      await store.AcknowledgeAsync(
          "tenant",
          incident.IncidentId,
          "42",
          Origin,
          cancellationToken);
      await store.SetSuppressionAsync(
          "tenant",
          incident.IncidentId,
          "maintenance",
          Origin.AddMinutes(1),
          Origin,
          cancellationToken);

      await store.ReconcileAsync(
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

      var expired = await store.GetByIdAsync(
          "tenant",
          incident.IncidentId,
          cancellationToken);
      await Assert.That(expired).IsNotNull();
      await Assert.That(expired!.OperatorState).IsEqualTo("unowned");
      await Assert.That(expired.AcknowledgedAt).IsNull();
      await Assert.That(expired.SuppressionReason).IsNull();
      await Assert.That(expired.Revision).IsGreaterThan(incident.Revision);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Acknowledgement_Taken_While_Waiting_Does_Not_Cover_Fresh_Severity(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-waiting-ack");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateProjectionCandidate("waiting-ack", Origin);
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
      var waiting = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken)).Incidents.Single();
      await store.AcknowledgeAsync(
          "tenant",
          waiting.IncidentId,
          "42",
          Origin.AddMinutes(1),
          cancellationToken);

      await store.ReconcileAsync(
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

      var refreshed = await store.GetByIdAsync(
          "tenant",
          waiting.IncidentId,
          cancellationToken);
      await Assert.That(refreshed).IsNotNull();
      await Assert.That(refreshed!.CurrentSeverity).IsEqualTo("warning");
      await Assert.That(refreshed.OperatorState).IsEqualTo("unowned");
      await Assert.That(refreshed.AcknowledgedAt).IsNull();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Suppression_Taken_While_Waiting_Does_Not_Cover_Fresh_Severity(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-waiting-suppression");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateProjectionCandidate(
          "waiting-suppression",
          Origin);
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
      var waiting = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(1),
          cancellationToken)).Incidents.Single();
      await store.SetSuppressionAsync(
          "tenant",
          waiting.IncidentId,
          "maintenance",
          Origin.AddHours(1),
          Origin.AddMinutes(1),
          cancellationToken);

      await store.ReconcileAsync(
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

      var refreshed = await store.GetByIdAsync(
          "tenant",
          waiting.IncidentId,
          cancellationToken);
      await Assert.That(refreshed).IsNotNull();
      await Assert.That(refreshed!.CurrentSeverity).IsEqualTo("warning");
      await Assert.That(refreshed.OperatorState).IsEqualTo("unowned");
      await Assert.That(refreshed.SuppressionReason).IsNull();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Acknowledgement_Before_Evidence_Gap_Covers_Same_Severity_Return(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-covered-gap");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateProjectionCandidate("covered-gap", Origin);
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var active = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken)).Incidents.Single();
      await store.AcknowledgeAsync(
          "tenant",
          active.IncidentId,
          "42",
          Origin,
          cancellationToken);
      await store.ReconcileAsync(
          [],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
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

      var refreshed = await store.GetByIdAsync(
          "tenant",
          active.IncidentId,
          cancellationToken);
      await Assert.That(refreshed).IsNotNull();
      await Assert.That(refreshed!.OperatorState).IsEqualTo("acknowledged");
      await Assert.That(refreshed.AcknowledgedByGitHubUserId)
          .IsEqualTo("42");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Cursor_Is_Invalidated_By_Attention_Mutations(
      CancellationToken cancellationToken)
  {
    foreach (var mutation in new[]
    {
      "escalation",
      "acknowledgement",
      "suppression",
      "resolution",
    })
    {
      await AssertCursorInvalidatedAsync(mutation, cancellationToken);
    }
  }

  [Test]
  public async Task Cursor_Preserves_Time_Sensitive_Attention_Snapshot(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-cursor-time");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidates = Enumerable.Range(0, 3)
          .Select(index => CreateCandidate(
              $"cursor-time-{index}",
              "tenant",
              TimeSpan.Zero) with
          {
            SourceObservedAt = Origin.AddMinutes(index),
            DashboardReceivedAt = Origin.AddMinutes(index),
          })
          .ToArray();
      await store.ReconcileAsync(
          candidates,
          [],
          Origin.AddMinutes(3),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var all = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(3),
          cancellationToken);
      foreach (var incident in all.Incidents)
      {
        await store.SetSuppressionAsync(
            "tenant",
            incident.IncidentId,
            "maintenance",
            Origin.AddMinutes(10),
            Origin.AddMinutes(3),
            cancellationToken);
      }
      var firstPage = await store.GetPageAsync(
          "tenant",
          AlertIncidentFilter.Active,
          1,
          null,
          Origin.AddMinutes(4),
          cancellationToken);
      var cursor = AlertIncidentCursor.ParseOrNull(firstPage.NextCursor);
      await Assert.That(cursor).IsNotNull();

      var continued = await store.GetPageAsync(
          "tenant",
          AlertIncidentFilter.Active,
          1,
          cursor,
          Origin.AddMinutes(20),
          cancellationToken);

      await Assert.That(continued.CursorInvalidated).IsFalse();
      await Assert.That(continued.Incidents).HasSingleItem();
      await Assert.That(continued.Incidents[0].IncidentId)
          .IsNotEqualTo(firstPage.Incidents[0].IncidentId);
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Pruned_History_Remains_Exactly_Addressable(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-pruned");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateProjectionCandidate("pruned", Origin);
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var incident = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken)).Incidents.Single();
      await store.ReconcileAsync(
          [],
          [CreateClearance(candidate, Origin.AddMinutes(1))],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [],
          [],
          Origin.AddDays(100),
          Origin.AddDays(1),
          100,
          cancellationToken);

      var visible = await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Resolved,
          100,
          Origin.AddDays(100),
          cancellationToken);
      var exact = await store.GetByIdAsync(
          "tenant",
          incident.IncidentId,
          cancellationToken);

      await Assert.That(visible.Incidents).IsEmpty();
      await Assert.That(exact).IsNotNull();
      await Assert.That(exact!.HistoryState).IsEqualTo("history-pruned");
      await Assert.That(exact.Evidence).IsNull();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Unverified_Legacy_Resolution_Uses_Ordinal_Zero_Then_Fresh_Ordinal_One(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-legacy-zero");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var legacyId = Guid.Parse(
          "22222222-2222-2222-2222-222222222222",
          System.Globalization.CultureInfo.InvariantCulture);
      await InsertLegacyResolvedAsync(
          factory,
          legacyId,
          "legacy-condition",
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      await store.ReconcileAsync(
          [],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var legacy = await store.GetByIdAsync(
          "tenant",
          legacyId,
          cancellationToken);
      await Assert.That(legacy).IsNotNull();
      await Assert.That(legacy!.EpisodeOrdinal).IsEqualTo(0);
      await Assert.That(legacy.ResolutionEvidence)
          .IsEqualTo("legacy-unverified");

      await store.ReconcileAsync(
          [CreateCandidate(
              "legacy-condition",
              "tenant",
              TimeSpan.Zero) with
          {
            Key = "legacy-condition",
            Reason = "legacy-condition",
            FirstObservedAt = Origin.AddMinutes(2),
            SourceObservedAt = Origin.AddMinutes(2),
            DashboardReceivedAt = Origin.AddMinutes(2),
          }],
          [],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var current = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken)).Incidents.Single();

      await Assert.That(current.EpisodeOrdinal).IsEqualTo(1);
      await Assert.That(current.PreviousIncidentId).IsEqualTo(legacyId);
      await Assert.That(current.Transition)
          .IsEqualTo("reopened-after-unverified-legacy-resolution");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Legacy_Predecessor_Compaction_Preserves_An_Expired_Boundary(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-legacy-boundary");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var legacyId = Guid.Parse(
          "33333333-3333-3333-3333-333333333333",
          System.Globalization.CultureInfo.InvariantCulture);
      await InsertLegacyResolvedAsync(
          factory,
          legacyId,
          "legacy-boundary",
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      await store.ReconcileAsync(
          [],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [CreateCandidate(
              "legacy-boundary",
              "tenant",
              TimeSpan.Zero) with
          {
            Key = "legacy-boundary",
            Reason = "legacy-boundary",
            FirstObservedAt = Origin.AddMinutes(2),
            SourceObservedAt = Origin.AddMinutes(2),
            DashboardReceivedAt = Origin.AddMinutes(2),
          }],
          [],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          0,
          cancellationToken);

      var current = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken)).Incidents.Single();
      await Assert.That(current.EpisodeOrdinal).IsEqualTo(1);
      await Assert.That(current.PreviousIncidentId).IsNull();
      await Assert.That(current.PreviousHistoryState)
          .IsEqualTo("legacy-resolution-history-expired");
      await Assert.That(current.Transition)
          .IsEqualTo("reopened-after-unverified-legacy-resolution");
      await Assert.That(await store.GetByIdAsync(
          "tenant",
          legacyId,
          cancellationToken)).IsNull();
      await Assert.That(await store.GetHistoryStateAsync(
          "tenant",
          legacyId,
          Origin.AddMinutes(2),
          cancellationToken)).IsEqualTo("history-expired");
      await Assert.That(await store.GetHistoryStateAsync(
          "other",
          legacyId,
          Origin.AddMinutes(2),
          cancellationToken)).IsEqualTo("history-unavailable");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Fresh_True_Uses_Legacy_Locator_When_Ordinal_Zero_Was_Compacted_First(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-legacy-compact-first");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var legacyId = Guid.Parse(
          "44444444-4444-4444-4444-444444444444",
          System.Globalization.CultureInfo.InvariantCulture);
      await InsertLegacyResolvedAsync(
          factory,
          legacyId,
          "legacy-compact-first",
          cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      await store.ReconcileAsync(
          [],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          0,
          cancellationToken,
          expiryLocatorRetention: TimeSpan.FromDays(30),
          maximumExpiryLocatorsPerTenant: 100);
      await Assert.That(await store.GetByIdAsync(
          "tenant",
          legacyId,
          cancellationToken)).IsNull();

      await store.ReconcileAsync(
          [CreateCandidate(
              "legacy-compact-first",
              "tenant",
              TimeSpan.Zero) with
          {
            Key = "legacy-compact-first",
            Reason = "legacy-compact-first",
            FirstObservedAt = Origin.AddMinutes(2),
            SourceObservedAt = Origin.AddMinutes(2),
            DashboardReceivedAt = Origin.AddMinutes(2),
          }],
          [],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken,
          expiryLocatorRetention: TimeSpan.FromDays(30),
          maximumExpiryLocatorsPerTenant: 100);

      var current = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken)).Incidents.Single();
      await Assert.That(current.EpisodeOrdinal).IsEqualTo(1);
      await Assert.That(current.PreviousIncidentId).IsNull();
      await Assert.That(current.PreviousHistoryState)
          .IsEqualTo("legacy-resolution-history-expired");
      await Assert.That(current.Transition)
          .IsEqualTo("reopened-after-unverified-legacy-resolution");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Recurrence_After_Compaction_Allocates_Next_Monotonic_Ordinal(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-compacted-recurrence");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidate = CreateProjectionCandidate(
          "compacted-recurrence",
          Origin);
      await store.ReconcileAsync(
          [candidate],
          [],
          Origin,
          Origin.AddDays(-90),
          100,
          cancellationToken);
      await store.ReconcileAsync(
          [],
          [CreateClearance(candidate, Origin.AddMinutes(1))],
          [],
          Origin.AddMinutes(1),
          Origin.AddDays(-90),
          0,
          cancellationToken,
          expiryLocatorRetention: TimeSpan.FromDays(30),
          maximumExpiryLocatorsPerTenant: 100);

      await store.ReconcileAsync(
          [candidate with
          {
            FirstObservedAt = Origin.AddMinutes(2),
            SourceObservedAt = Origin.AddMinutes(2),
            DashboardReceivedAt = Origin.AddMinutes(2),
          }],
          [],
          Origin.AddMinutes(2),
          Origin.AddDays(-90),
          100,
          cancellationToken,
          expiryLocatorRetention: TimeSpan.FromDays(30),
          maximumExpiryLocatorsPerTenant: 100);

      var recurrence = (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin.AddMinutes(2),
          cancellationToken)).Incidents.Single();
      await Assert.That(recurrence.EpisodeOrdinal).IsEqualTo(2);
      await Assert.That(recurrence.PreviousIncidentId).IsNull();
      await Assert.That(recurrence.PreviousHistoryState)
          .IsEqualTo("history-expired");
      await Assert.That(recurrence.Transition).IsEqualTo("recurrence");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Expiry_Locators_Are_Bounded_Per_Tenant_Under_Sustained_Churn(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-locator-churn");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var incidentIds = new List<Guid>();
      for (var index = 0; index < 5; index++)
      {
        var observedAt = Origin.AddMinutes(index * 2);
        var candidate = CreateCandidate(
            $"locator-{index}",
            "tenant",
            TimeSpan.Zero) with
        {
          FirstObservedAt = observedAt,
          SourceObservedAt = observedAt,
          DashboardReceivedAt = observedAt,
        };
        await store.ReconcileAsync(
            [candidate],
            [],
            observedAt,
            Origin.AddDays(-90),
            100,
            cancellationToken,
            expiryLocatorRetention: TimeSpan.FromDays(30),
            maximumExpiryLocatorsPerTenant: 2);
        incidentIds.Add((await store.GetAsync(
            "tenant",
            AlertIncidentFilter.Active,
            100,
            observedAt,
            cancellationToken)).Incidents.Single().IncidentId);
        await store.ReconcileAsync(
            [],
            [CreateClearance(candidate, observedAt.AddMinutes(1))],
            [],
            observedAt.AddMinutes(1),
            Origin.AddDays(-90),
            0,
            cancellationToken,
            expiryLocatorRetention: TimeSpan.FromDays(30),
            maximumExpiryLocatorsPerTenant: 2);
      }

      await Assert.That(await CountExpiryLocatorsAsync(
          factory,
          "tenant",
          cancellationToken)).IsEqualTo(2);
      await Assert.That(await store.GetHistoryStateAsync(
          "tenant",
          incidentIds[0],
          Origin.AddMinutes(10),
          cancellationToken)).IsEqualTo("history-unavailable");
      await Assert.That(await store.GetHistoryStateAsync(
          "tenant",
          incidentIds[^1],
          Origin.AddMinutes(10),
          cancellationToken)).IsEqualTo("history-expired");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  [Test]
  public async Task Locator_Ceiling_Retains_Newest_Terminal_History_In_One_Batch(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("projection-locator-terminal-order");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var oldestId = Guid.Parse(
          "ffffffff-ffff-ffff-ffff-ffffffffffff",
          System.Globalization.CultureInfo.InvariantCulture);
      var middleId = Guid.Parse(
          "88888888-8888-8888-8888-888888888888",
          System.Globalization.CultureInfo.InvariantCulture);
      var newestId = Guid.Parse(
          "00000000-0000-0000-0000-000000000001",
          System.Globalization.CultureInfo.InvariantCulture);
      await InsertLegacyResolvedAsync(
          factory,
          oldestId,
          "locator-oldest",
          cancellationToken,
          Origin);
      await InsertLegacyResolvedAsync(
          factory,
          middleId,
          "locator-middle",
          cancellationToken,
          Origin.AddMinutes(1));
      await InsertLegacyResolvedAsync(
          factory,
          newestId,
          "locator-newest",
          cancellationToken,
          Origin.AddMinutes(2));
      var store = new SqliteAlertIncidentStore(factory);

      await store.ReconcileAsync(
          [],
          [],
          Origin.AddMinutes(3),
          Origin.AddDays(-90),
          0,
          cancellationToken,
          expiryLocatorRetention: TimeSpan.FromDays(30),
          maximumExpiryLocatorsPerTenant: 2);

      await Assert.That(await store.GetHistoryStateAsync(
          "tenant",
          oldestId,
          Origin.AddMinutes(3),
          cancellationToken)).IsEqualTo("history-unavailable");
      await Assert.That(await store.GetHistoryStateAsync(
          "tenant",
          middleId,
          Origin.AddMinutes(3),
          cancellationToken)).IsEqualTo("history-expired");
      await Assert.That(await store.GetHistoryStateAsync(
          "tenant",
          newestId,
          Origin.AddMinutes(3),
          cancellationToken)).IsEqualTo("history-expired");
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  private static AlertCandidate CreateProjectionCandidate(
      string key,
      DateTimeOffset observedAt) =>
      CreateCandidate(key, "tenant", TimeSpan.Zero) with
      {
        Key = key,
        Reason = key,
        FirstObservedAt = observedAt,
        SourceObservedAt = observedAt,
        DashboardReceivedAt = observedAt,
        RuleFamily = "test-rule",
        RuleInterpretationVersion = 1,
        GroupingPolicyVersion = 1,
        IncidentFamily = "test-visibility",
        CanonicalTargetScope = "node:11111111-1111-1111-1111-111111111111",
        InvestigationClass = "restore-visibility",
        EvidenceDependency = "connector-boundary",
        GroupingReasons =
        [
            "same-authoritative-target",
            "same-investigation-class",
            "shared-unavailable-evidence",
        ],
      };

  private static async Task<AlertIncident> ProjectAsync(
      string label,
      IReadOnlyList<AlertCandidate> candidates,
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath(label);
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      foreach (var candidate in candidates)
      {
        await store.ReconcileAsync(
            [candidate],
            [],
            Origin,
            Origin.AddDays(-90),
            100,
            cancellationToken);
      }
      return (await store.GetAsync(
          "tenant",
          AlertIncidentFilter.Active,
          100,
          Origin,
          cancellationToken)).Incidents.Single();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  private static async Task InsertLegacyResolvedAsync(
      SqliteConnectionFactory factory,
      Guid incidentId,
      string alertKey,
      CancellationToken cancellationToken,
      DateTimeOffset? observedAt = null)
  {
    var terminalAt = observedAt ?? Origin;
    await using var connection = await factory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO alert_incidents (
            incident_id,
            alert_key,
            tenant_id,
            node_id,
            profile_id,
            kind,
            severity,
            status,
            title,
            summary,
            reason,
            link,
            first_observed_at,
            trigger_after,
            last_observed_at,
            triggered_at,
            resolved_at,
            condition_state,
            operator_state,
            current_severity,
            last_confirmed_severity,
            peak_severity,
            created_at,
            updated_at)
        VALUES (
            $incidentId,
            $alertKey,
            'tenant',
            '11111111-1111-1111-1111-111111111111',
            'default',
            'test-alert',
            'critical',
            'resolved',
            'Legacy incident',
            'Legacy incident summary.',
            'legacy-condition',
            '/tenants/tenant/nodes/11111111-1111-1111-1111-111111111111',
            $origin,
            $origin,
            $origin,
            $origin,
            $origin,
            'legacy-unverified',
            'unowned',
            NULL,
            'critical',
            'critical',
            $origin,
            $origin);
        """;
    command.Parameters.AddWithValue(
        "$incidentId",
        incidentId.ToString("D"));
    command.Parameters.AddWithValue("$alertKey", alertKey);
    command.Parameters.AddWithValue(
        "$origin",
        terminalAt.ToUniversalTime().ToString(
            "O",
            System.Globalization.CultureInfo.InvariantCulture));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task AssertCursorInvalidatedAsync(
      string mutation,
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath($"projection-cursor-{mutation}");
    try
    {
      var factory = await CreateDatabaseAsync(databasePath, cancellationToken);
      var store = new SqliteAlertIncidentStore(factory);
      var candidates = Enumerable.Range(0, 3)
          .Select(index => CreateCandidate(
              $"cursor-{index}",
              "tenant",
              TimeSpan.Zero) with
          {
            SourceObservedAt = Origin.AddMinutes(index),
            DashboardReceivedAt = Origin.AddMinutes(index),
          })
          .ToArray();
      await store.ReconcileAsync(
          candidates,
          [],
          Origin.AddMinutes(3),
          Origin.AddDays(-90),
          100,
          cancellationToken);
      var firstPage = await store.GetPageAsync(
          "tenant",
          AlertIncidentFilter.Active,
          1,
          null,
          Origin.AddMinutes(3),
          cancellationToken);
      var cursor = AlertIncidentCursor.ParseOrNull(firstPage.NextCursor);
      await Assert.That(cursor).IsNotNull();
      var selected = firstPage.Incidents.Single();
      var candidate = candidates.Single(item =>
          item.Reason == selected.Reason);
      switch (mutation)
      {
        case "escalation":
          await store.ReconcileAsync(
              candidates.Select(item => item.Key == candidate.Key
                  ? item with
                  {
                    Severity = "critical",
                    SourceObservedAt = Origin.AddMinutes(4),
                    DashboardReceivedAt = Origin.AddMinutes(4),
                  }
                  : item).ToArray(),
              [],
              Origin.AddMinutes(4),
              Origin.AddDays(-90),
              100,
              cancellationToken);
          break;
        case "acknowledgement":
          await store.AcknowledgeAsync(
              "tenant",
              selected.IncidentId,
              "1",
              Origin.AddMinutes(4),
              cancellationToken);
          break;
        case "suppression":
          await store.SetSuppressionAsync(
              "tenant",
              selected.IncidentId,
              "maintenance",
              Origin.AddHours(1),
              Origin.AddMinutes(4),
              cancellationToken);
          break;
        case "resolution":
          await store.ReconcileAsync(
              candidates.Where(item => item.Key != candidate.Key).ToArray(),
              [CreateClearance(candidate, Origin.AddMinutes(4))],
              [],
              Origin.AddMinutes(4),
              Origin.AddDays(-90),
              100,
              cancellationToken);
          break;
        default:
          throw new InvalidOperationException(
              $"Unsupported mutation '{mutation}'.");
      }

      var continued = await store.GetPageAsync(
          "tenant",
          AlertIncidentFilter.Active,
          1,
          cursor,
          Origin.AddMinutes(4),
          cancellationToken);
      await Assert.That(continued.CursorInvalidated).IsTrue();
      await Assert.That(continued.Incidents).IsEmpty();
      await Assert.That(continued.NextCursor).IsNull();
    }
    finally
    {
      Cleanup(databasePath);
    }
  }

  private static async Task<long> CountExpiryLocatorsAsync(
      SqliteConnectionFactory factory,
      string tenantId,
      CancellationToken cancellationToken)
  {
    await using var connection = await factory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT COUNT(*)
        FROM actionable_incident_expiry_locators
        WHERE tenant_id = $tenantId;
        """;
    command.Parameters.AddWithValue("$tenantId", tenantId);
    return Convert.ToInt64(
        await command.ExecuteScalarAsync(cancellationToken),
        System.Globalization.CultureInfo.InvariantCulture);
  }
}
