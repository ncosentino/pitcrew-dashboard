using System.Globalization;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class AlertRuleEvaluatorTests
{
  private static readonly DateTimeOffset Now = new(
      2026,
      7,
      28,
      2,
      0,
      0,
      TimeSpan.Zero);

  [Test]
  public async Task Offline_Node_Suppresses_Profile_Diagnoses()
  {
    var profile = CreateProfile(
        Now,
        slots:
        [
            CreateStoppedSlot(
                new WorkerLastExitDiagnostic(
                    Now,
                    "oom-killed",
                    137,
                    9,
                    true,
                    "docker-inspect")),
        ]);
    var snapshot = CreateSnapshot(
        profile,
        lastSeenAt: Now.AddMinutes(-10));

    var candidates = AlertRuleEvaluator.Evaluate(
        snapshot,
        CreateOptions(),
        Now).Candidates;

    await Assert.That(candidates).HasSingleItem();
    await Assert.That(candidates[0].Kind)
        .IsEqualTo("connector-offline");
  }

  [Test]
  public async Task Stale_Manager_Suppresses_Specific_Failure_Diagnoses()
  {
    var profile = CreateProfile(
        Now.AddMinutes(-10),
        subsystemHealth: CreateDegradedHealth());

    var evaluation = AlertRuleEvaluator.Evaluate(
        CreateSnapshot(profile, Now),
        CreateOptions(),
        Now);

    await Assert.That(evaluation.Candidates).HasSingleItem();
    await Assert.That(evaluation.Candidates[0].Kind)
        .IsEqualTo("manager-stale");
    await Assert.That(evaluation.Suppressions.Count).IsEqualTo(15);
    await Assert.That(evaluation.Suppressions.Count(
        suppression => suppression.ProfileId == "default"))
        .IsEqualTo(15);
  }

  [Test]
  public async Task Current_Proven_Evidence_Produces_Specific_Incidents()
  {
    var profile = CreateProfile(
        Now,
        subsystemHealth: CreateDegradedHealth(),
        capacityEvidence: new ManagerCapacityEvidence(
            new CapacityDeficitEvidence(
                Now,
                "current",
                4,
                2,
                0,
                0,
                0,
                2,
                2,
                0,
                "docker-unavailable",
                "daemon unavailable"),
            []),
        slots:
        [
            CreateStoppedSlot(
                new WorkerLastExitDiagnostic(
                    Now,
                    "oom-killed",
                    137,
                    9,
                    true,
                    "docker-inspect")),
        ]);
    var evidence = new AlertProfileEvidence(
        profile,
        CurrentJournal(),
        [],
        new AlertCommandEvidence(
            Guid.NewGuid(),
            "capacity",
            "failed",
            Now,
            null,
            "setup failed"),
        null);

    var candidates = AlertRuleEvaluator.Evaluate(
        CreateSnapshot(evidence, Now),
        CreateOptions(),
        Now).Candidates;

    await Assert.That(candidates.Count).IsEqualTo(4);
    await Assert.That(candidates.Count(
        candidate => candidate.Kind == "subsystem-failure"))
        .IsEqualTo(1);
    await Assert.That(candidates.Count(
        candidate => candidate.Kind == "capacity-deficit"))
        .IsEqualTo(1);
    await Assert.That(candidates.Count(
        candidate => candidate.Kind == "worker-oom"))
        .IsEqualTo(1);
    await Assert.That(candidates.Count(
        candidate => candidate.Kind == "command-failure"))
        .IsEqualTo(1);
  }

  [Test]
  public async Task One_Operation_Failure_Does_Not_Alert_But_Repeated_Failures_Do()
  {
    var oneFailure = CreateProfile(
        Now,
        journal: CreateJournal(CreateEvent(1)));
    var repeated = CreateProfile(
        Now,
        journal: CreateJournal(CreateEvent(3)));

    var transientCandidates = AlertRuleEvaluator.Evaluate(
        CreateSnapshot(oneFailure, Now),
        CreateOptions(),
        Now).Candidates;
    var repeatedCandidates = AlertRuleEvaluator.Evaluate(
        CreateSnapshot(repeated, Now),
        CreateOptions(),
        Now).Candidates;

    await Assert.That(transientCandidates).IsEmpty();
    await Assert.That(repeatedCandidates).HasSingleItem();
    await Assert.That(repeatedCandidates[0].Kind)
        .IsEqualTo("manager-operation-failure");
  }

  [Test]
  public async Task Explicit_Subsystem_Unavailability_Alerts_Without_Inventing_A_Failure_Count()
  {
    var profile = CreateProfile(
        Now,
        subsystemHealth: new ManagerSubsystemHealth(
            new SubsystemHealthSummary(
                "unavailable",
                Now,
                0,
                null,
                null,
                null),
            new SubsystemHealthSummary(
                "healthy",
                Now,
                0,
                null,
                null,
                null)));

    var candidates = AlertRuleEvaluator.Evaluate(
        CreateSnapshot(profile, Now),
        CreateOptions(),
        Now).Candidates;

    await Assert.That(candidates).HasSingleItem();
    await Assert.That(candidates[0].Kind)
        .IsEqualTo("subsystem-failure");
    await Assert.That(candidates[0].Reason)
        .IsEqualTo("docker-unavailable");
  }

  [Test]
  public async Task Sustained_Complete_Samples_Produce_Configured_Resource_Alerts()
  {
    var options = CreateOptions();
    options.AlertResourcePressureSamples = 2;
    options.AlertNetworkBytesPerSecond = 100;
    var profile = new AlertProfileEvidence(
        CreateProfile(Now),
        CurrentJournal(),
        [
            CreateResourceSample(
                Now.AddSeconds(-30),
                1_000),
            CreateResourceSample(
                Now.AddSeconds(-15),
                3_000),
            CreateResourceSample(
                Now,
                5_000),
        ],
        null,
        null);

    var candidates = AlertRuleEvaluator.Evaluate(
        CreateSnapshot(profile, Now),
        options,
        Now).Candidates;

    await Assert.That(candidates.Count).IsEqualTo(3);
    await Assert.That(candidates.Count(
        candidate => candidate.Kind == "resource-cpu-pressure"))
        .IsEqualTo(1);
    await Assert.That(candidates.Count(
        candidate => candidate.Kind == "resource-memory-pressure"))
        .IsEqualTo(1);
    await Assert.That(candidates.Count(
        candidate => candidate.Kind == "resource-network-pressure"))
        .IsEqualTo(1);
  }

  [Test]
  public async Task Unavailable_And_Stale_Evidence_Do_Not_Produce_Specific_Diagnoses()
  {
    var options = CreateOptions();
    options.AlertResourcePressureSamples = 2;
    var unavailableDeficit = new ManagerCapacityEvidence(
        new CapacityDeficitEvidence(
            Now,
            "unavailable",
            4,
            0,
            0,
            0,
            0,
            null,
            4,
            null,
            "unknown",
            null),
        []);
    var profile = new AlertProfileEvidence(
        CreateProfile(
            Now,
            capacityEvidence: unavailableDeficit),
        CurrentJournal(),
        [
            CreateResourceSample(Now.AddSeconds(-15), 1_000) with
            {
              Status = "partial",
            },
            CreateResourceSample(Now, 3_000),
        ],
        null,
        null);

    var evaluation = AlertRuleEvaluator.Evaluate(
        CreateSnapshot(profile, Now),
        options,
        Now);

    await Assert.That(evaluation.Candidates).IsEmpty();
    await Assert.That(evaluation.Suppressions.Count).IsGreaterThanOrEqualTo(3);
  }

  [Test]
  public async Task Current_Journal_Gaps_Remain_Explicit_Incidents()
  {
    var profile = new AlertProfileEvidence(
        CreateProfile(Now),
        new AlertJournalEvidence(
            "current",
            2,
            3,
            4,
            1,
            0,
            Now.AddHours(-1)),
        [],
        null,
        null);

    var candidates = AlertRuleEvaluator.Evaluate(
        CreateSnapshot(profile, Now),
        CreateOptions(),
        Now).Candidates;

    await Assert.That(candidates.Count).IsEqualTo(3);
    await Assert.That(candidates.Count(
        candidate => candidate.Kind == "journal-undelivered"))
        .IsEqualTo(1);
    await Assert.That(candidates.Count(
        candidate => candidate.Kind == "journal-discontinuity"))
        .IsEqualTo(1);
    await Assert.That(candidates.Count(
        candidate => candidate.Kind == "history-expired"))
        .IsEqualTo(1);
  }

  private static FleetDashboardOptions CreateOptions() =>
      new()
      {
        AlertDebounceSeconds = 0,
        AlertManagerStaleAfterSeconds = 120,
        AlertRepeatedFailureCount = 3,
      };

  private static AlertEvidenceSnapshot CreateSnapshot(
      ManagerObservedState profile,
      DateTimeOffset? lastSeenAt) =>
      CreateSnapshot(
          new AlertProfileEvidence(
              profile,
              CurrentJournal(),
              [],
              null,
              null),
          lastSeenAt);

  private static AlertEvidenceSnapshot CreateSnapshot(
      AlertProfileEvidence profile,
      DateTimeOffset? lastSeenAt) =>
      new(
          [
              new AlertNodeEvidence(
                  "tenant",
                  Guid.Parse(
                      "11111111-1111-1111-1111-111111111111",
                      CultureInfo.InvariantCulture),
                  "Node",
                  Now.AddDays(-1),
                  lastSeenAt,
                  false,
                  [profile]),
          ]);

  private static ManagerObservedState CreateProfile(
      DateTimeOffset observedAt,
      ManagerOperationJournal? journal = null,
      ManagerSubsystemHealth? subsystemHealth = null,
      ManagerCapacityEvidence? capacityEvidence = null,
      IReadOnlyList<ObservedSlotState>? slots = null) =>
      new(
          1,
          12,
          "default",
          "manager",
          "running",
          observedAt,
          "repository",
          1,
          new string('a', 64),
          "accepted",
          4,
          slots?.Count(slot => slot.ProcessRunning) ?? 0,
          0,
          slots ?? [],
          null,
          4,
          null,
          null,
          null,
          journal ?? CreateJournal(),
          subsystemHealth,
          capacityEvidence);

  private static ManagerOperationJournal CreateJournal(
      params ManagerEvent[] events) =>
      new(
          "current",
          64,
          events.Length == 0 ? null : events.Max(item => item.Sequence),
          0,
          events);

  private static ManagerEvent CreateEvent(int consecutiveFailures) =>
      new(
          1,
          "manager",
          Now,
          "jit",
          "jit-config-generate",
          "target",
          "failed",
          10,
          consecutiveFailures,
          consecutiveFailures,
          null,
          "jit-failed",
          "generation failed");

  private static ManagerSubsystemHealth CreateDegradedHealth() =>
      new(
          new SubsystemHealthSummary(
              "degraded",
              Now,
              3,
              Now.AddMinutes(1),
              null,
              new SubsystemOperationEvidence(
                  "docker-ping",
                  Now,
                  100,
                  "docker-unavailable",
                  "daemon unavailable")),
          new SubsystemHealthSummary(
              "healthy",
              Now,
              0,
              null,
              null,
              null));

  private static ObservedSlotState CreateStoppedSlot(
      WorkerLastExitDiagnostic? lastExit) =>
      new(
          "slot-1",
          "owner/repo",
          true,
          false,
          "backoff",
          3,
          30,
          Now,
          null,
          "idle",
          null,
          "disconnected",
          $"sha256:{new string('b', 64)}",
          lastExit);

  private static AlertJournalEvidence CurrentJournal() =>
      new(
          "current",
          0,
          0,
          0,
          0,
          0,
          null);

  private static AlertResourceSample CreateResourceSample(
      DateTimeOffset observedAt,
      long networkBytes) =>
      new(
          observedAt,
          "available",
          3.8,
          4,
          950,
          1000,
          networkBytes,
          0);
}
