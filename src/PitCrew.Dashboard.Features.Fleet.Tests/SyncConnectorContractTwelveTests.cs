using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SyncConnectorContractTwelveTests
{
  private static readonly DateTimeOffset ObservedAt = new(
      2026,
      7,
      26,
      12,
      0,
      0,
      TimeSpan.Zero);

  [Test]
  public async Task IsValidProfile_Accepts_Complete_Contract_Twelve_Projection()
  {
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            CreateAutoscaledProfile()))
        .IsTrue()
        .Because("a complete contract-12 projection satisfies the contract");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            CreateFixedProfile()))
        .IsTrue()
        .Because("a fixed contract-12 profile reports fixed deficit evidence");
  }

  [Test]
  public async Task IsValidProfile_Accepts_Contract_Eleven_Observations_Without_Diagnostics()
  {
    var profile = CreateAutoscaledProfile() with
    {
      ManagerContractVersion = 11,
      OperationJournal = null,
      SubsystemHealth = null,
      CapacityEvidence = null,
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile))
        .IsTrue()
        .Because("contract-11 observations remain accepted with unavailable evidence");
  }

  [Test]
  public async Task IsValidProfile_Rejects_Contract_Twelve_Observations_Missing_Diagnostics()
  {
    var profile = CreateAutoscaledProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with { OperationJournal = null }))
        .IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with { SubsystemHealth = null }))
        .IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with { CapacityEvidence = null }))
        .IsFalse();
  }

  [Test]
  public async Task IsValidProfile_Accepts_Unavailable_Stale_And_Measured_Zero_Evidence()
  {
    var profile = CreateAutoscaledProfile();
    var unavailable = profile with
    {
      OperationJournal = new ManagerOperationJournal(
          "unavailable",
          64,
          null,
          0,
          []),
      SubsystemHealth = new ManagerSubsystemHealth(
          UnknownSubsystem(),
          UnknownSubsystem()),
      CapacityEvidence = new ManagerCapacityEvidence(
          null,
          [
              TargetDeficit() with
              {
                Freshness = "unavailable",
                EligibleWorkers = null,
                EligibilityDeficit = null,
                Reason = "unknown",
              },
          ]),
    };
    var measuredZero = profile with
    {
      CapacityEvidence = new ManagerCapacityEvidence(
          null,
          [
              TargetDeficit() with
              {
                Freshness = "stale",
                EligibleWorkers = 0,
                EligibilityDeficit = 0,
                LocalDeficit = 0,
                Reason = "none",
              },
          ]),
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(unavailable))
        .IsTrue()
        .Because("an unavailable journal and unknown subsystems are observable states");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(measuredZero))
        .IsTrue()
        .Because("zero eligible workers is a measurement, not missing evidence");
  }

  [Test]
  public async Task IsValidProfile_Rejects_Malformed_And_Oversized_Events()
  {
    var profile = CreateAutoscaledProfile();

    await Assert.That(IsValidJournal(
            profile,
            Journal([Event() with { Evidence = "https://example.com/token" }])))
        .IsFalse()
        .Because("evidence excludes the characters that could relay a URL or token");
    await Assert.That(IsValidJournal(
            profile,
            Journal([Event() with { Evidence = new string('a', 161) }])))
        .IsFalse()
        .Because("oversized evidence is rejected");
    await Assert.That(IsValidJournal(
            profile,
            Journal([Event() with { Operation = "docker-restart" }])))
        .IsFalse()
        .Because("the operation vocabulary is closed");
    await Assert.That(IsValidJournal(
            profile,
            Journal([Event() with { Subsystem = "network" }])))
        .IsFalse()
        .Because("the subsystem vocabulary is closed");
    await Assert.That(IsValidJournal(
            profile,
            Journal([Event() with { Outcome = "succeeded", Reason = "timeout" }])))
        .IsFalse()
        .Because("a succeeded operation cannot report a failure reason");
    await Assert.That(IsValidJournal(
            profile,
            Journal([Event() with { Outcome = "failed", Reason = "none" }])))
        .IsFalse()
        .Because("a failed operation must report why it failed");
    await Assert.That(IsValidJournal(
            profile,
            Journal([
                Event() with
                {
                  Outcome = "retry-scheduled",
                  Reason = "retry-backoff",
                  RetryAt = null,
                },
            ])))
        .IsFalse()
        .Because("a scheduled retry must report when it retries");
    await Assert.That(IsValidJournal(
            profile,
            Journal([Event() with { Target = new string('t', 129) }])))
        .IsFalse()
        .Because("target identity is bounded to observed-state keys");
    await Assert.That(IsValidJournal(
            profile,
            Journal(
                [.. Enumerable
                    .Range(1, 65)
                    .Select(sequence => Event() with { Sequence = sequence })],
                highestSequence: 65)))
        .IsFalse()
        .Because("the retained journal window is bounded");
    await Assert.That(IsValidJournal(
            profile,
            new ManagerOperationJournal(
                "truncated",
                64,
                12,
                0,
                [Event()])))
        .IsFalse()
        .Because("a truncated journal must report the discarded entries");
    await Assert.That(IsValidJournal(
            profile,
            new ManagerOperationJournal(
                "unavailable",
                64,
                12,
                0,
                [Event()])))
        .IsFalse()
        .Because("an unavailable journal cannot carry retained events");
  }

  [Test]
  public async Task IsValidProfile_Deduplicates_Events_By_Contract_Identity()
  {
    var profile = CreateAutoscaledProfile();
    var duplicated = Journal(
        [
            Event() with { Sequence = 12 },
            Event() with
            {
              Sequence = 12,
              ManagerInstanceId = "manager-instance-2",
              ObservedAt = ObservedAt.AddSeconds(5),
            },
        ],
        highestSequence: 12);

    await Assert.That(IsValidJournal(profile, duplicated))
        .IsFalse()
        .Because("one durable sequence identifies exactly one event");
  }

  [Test]
  public async Task IsValidProfile_Accepts_Journal_Continuity_Across_Manager_Restart()
  {
    var profile = CreateAutoscaledProfile();
    var restarted = Journal(
        [
            Event() with
            {
              Sequence = 11,
              ManagerInstanceId = "manager-instance-1",
              Operation = "manager-shutdown",
              ObservedAt = ObservedAt.AddMinutes(-5),
            },
            Event() with
            {
              Sequence = 12,
              ManagerInstanceId = "manager-instance",
              Operation = "manager-start",
              ObservedAt = ObservedAt.AddMinutes(-4),
            },
            Event() with
            {
              Sequence = 13,
              ManagerInstanceId = "manager-instance",
              Operation = "journal-restore",
            },
        ],
        highestSequence: 13);

    await Assert.That(IsValidJournal(profile, restarted))
        .IsTrue()
        .Because("durable sequences continue across a manager restart");
  }

  [Test]
  public async Task IsValidProfile_Rejects_Sequences_Above_The_Reported_Window()
  {
    var profile = CreateAutoscaledProfile();

    await Assert.That(IsValidJournal(
            profile,
            Journal([Event() with { Sequence = 99 }], highestSequence: 12)))
        .IsFalse();
    await Assert.That(IsValidJournal(
            profile,
            new ManagerOperationJournal("current", 64, null, 0, [Event()])))
        .IsFalse()
        .Because("a retained event requires a reported highest sequence");
  }

  [Test]
  public async Task IsValidProfile_Rejects_Inconsistent_Subsystem_Health()
  {
    var profile = CreateAutoscaledProfile();

    await Assert.That(IsValidHealth(
            profile,
            HealthySubsystem() with { LastSuccess = null }))
        .IsFalse()
        .Because("a healthy subsystem reports the operation that succeeded");
    await Assert.That(IsValidHealth(
            profile,
            DegradedSubsystem() with { ConsecutiveFailures = 0 }))
        .IsFalse()
        .Because("a degraded subsystem reports at least one failure");
    await Assert.That(IsValidHealth(
            profile,
            UnknownSubsystem() with { LastFailure = FailureEvidence() }))
        .IsFalse()
        .Because("an unknown subsystem has no observed operation");
    await Assert.That(IsValidHealth(
            profile,
            HealthySubsystem() with
            {
              LastSuccess = SuccessEvidence() with { Operation = "docker-restart" },
            }))
        .IsFalse()
        .Because("the operation vocabulary is closed");
  }

  [Test]
  public async Task IsValidProfile_Accepts_Capacity_Evidence_The_Manager_Could_Not_Measure()
  {
    var fixedProfile = CreateFixedProfile();
    var autoscaled = CreateAutoscaledProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(fixedProfile with
    {
      CapacityEvidence = new ManagerCapacityEvidence(
          UnavailableDeficit(),
          []),
    }))
        .IsTrue()
        .Because(
            "a manager that cannot measure capacity publishes zero target slots while its " +
            "accepted desired slots stay nonzero");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(autoscaled with
    {
      CapacityEvidence = new ManagerCapacityEvidence(
          null,
          [
              TargetDeficit() with
              {
                Key = "repo:example/retired",
                TargetSlots = 8,
              },
          ]),
    }))
        .IsTrue()
        .Because(
            "per-target evidence and the autoscaling projection are measured independently, so " +
            "a bounded key or a newer activation target is not a malformed observation");
  }

  [Test]
  public async Task IsValidProfile_Rejects_Inconsistent_Capacity_Deficits()
  {
    var autoscaled = CreateAutoscaledProfile();
    var fixedProfile = CreateFixedProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(autoscaled with
    {
      CapacityEvidence = new ManagerCapacityEvidence(
          FixedDeficit(),
          [TargetDeficit()]),
    }))
        .IsFalse()
        .Because("an autoscaled profile reports per-target evidence instead of fixed evidence");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(fixedProfile with
    {
      CapacityEvidence = new ManagerCapacityEvidence(
          null,
          [TargetDeficit()]),
    }))
        .IsFalse()
        .Because("a fixed profile reports fixed evidence instead of per-target evidence");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(autoscaled with
    {
      CapacityEvidence = new ManagerCapacityEvidence(
          null,
          [TargetDeficit() with { EligibilityDeficit = null }]),
    }))
        .IsFalse()
        .Because("eligible evidence and its deficit are available together");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(autoscaled with
    {
      CapacityEvidence = new ManagerCapacityEvidence(
          null,
          [TargetDeficit() with { LocalDeficit = 2, Reason = "none" }]),
    }))
        .IsFalse()
        .Because("a reported shortfall carries a manager-supplied reason");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(autoscaled with
    {
      CapacityEvidence = new ManagerCapacityEvidence(
          null,
          [
              TargetDeficit() with
              {
                Freshness = "unavailable",
                Reason = "docker-failed",
              },
          ]),
    }))
        .IsFalse()
        .Because("unavailable evidence cannot name a blocking reason");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(autoscaled with
    {
      CapacityEvidence = new ManagerCapacityEvidence(
          null,
          [
              TargetDeficit(),
              TargetDeficit(),
          ]),
    }))
        .IsFalse()
        .Because("one target reports one deficit projection");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(autoscaled with
    {
      CapacityEvidence = new ManagerCapacityEvidence(
          null,
          [TargetDeficit() with { Evidence = "docker: unavailable at 10.0.0.1" }]),
    }))
        .IsFalse()
        .Because("deficit evidence is sanitized like every other manager evidence string");
  }

  private static bool IsValidJournal(
      ManagerObservedState profile,
      ManagerOperationJournal journal) =>
      SyncConnectorUnitOfWork.IsValidProfile(profile with
      {
        OperationJournal = journal,
      });

  private static bool IsValidHealth(
      ManagerObservedState profile,
      SubsystemHealthSummary docker) =>
      SyncConnectorUnitOfWork.IsValidProfile(profile with
      {
        SubsystemHealth = new ManagerSubsystemHealth(
            docker,
            HealthySubsystem()),
      });

  private static ManagerOperationJournal Journal(
      IReadOnlyList<ManagerEvent> events) =>
      Journal(events, events.Max(managerEvent => managerEvent.Sequence));

  private static ManagerOperationJournal Journal(
      IReadOnlyList<ManagerEvent> events,
      long highestSequence) =>
      new(
          "current",
          64,
          highestSequence,
          0,
          events);

  private static ManagerEvent Event() =>
      new(
          12,
          "manager-instance",
          ObservedAt,
          "docker",
          "docker-run",
          "repo:example/project",
          "failed",
          420,
          2,
          3,
          ObservedAt.AddSeconds(30),
          "docker-failed",
          "Docker refused to start the worker container.");

  private static SubsystemOperationEvidence SuccessEvidence() =>
      new(
          "docker-ping",
          ObservedAt.AddSeconds(-30),
          12,
          "none",
          null);

  private static SubsystemOperationEvidence FailureEvidence() =>
      new(
          "docker-run",
          ObservedAt.AddSeconds(-10),
          4200,
          "docker-failed",
          "Docker refused to start the worker container.");

  private static SubsystemHealthSummary HealthySubsystem() =>
      new(
          "healthy",
          ObservedAt,
          0,
          null,
          SuccessEvidence(),
          null);

  private static SubsystemHealthSummary DegradedSubsystem() =>
      new(
          "degraded",
          ObservedAt,
          3,
          ObservedAt.AddSeconds(30),
          SuccessEvidence(),
          FailureEvidence());

  private static SubsystemHealthSummary UnknownSubsystem() =>
      new(
          "unknown",
          ObservedAt,
          0,
          null,
          null,
          null);

  private static CapacityDeficitEvidence FixedDeficit() =>
      new(
          ObservedAt,
          "current",
          2,
          1,
          1,
          0,
          0,
          1,
          1,
          1,
          "launch-pending",
          "Worker launch is pending.");

  private static CapacityDeficitEvidence UnavailableDeficit() =>
      new(
          ObservedAt,
          "unavailable",
          0,
          0,
          0,
          0,
          0,
          null,
          0,
          null,
          "unknown",
          null);

  private static TargetCapacityDeficitEvidence TargetDeficit() =>
      new(
          "repo:example/project",
          "https://github.com/example/project",
          ObservedAt,
          "current",
          2,
          1,
          1,
          0,
          0,
          1,
          1,
          1,
          "launch-pending",
          "Worker launch is pending.");

  internal static ManagerObservedState CreateAutoscaledProfile() =>
      CreateBaseProfile() with
      {
        ConfiguredSlots = 8,
        Autoscaling = new ManagerAutoscalingState(
            "scale-set",
            "running",
            0,
            8,
            2,
            2,
            2,
            0,
            0,
            1,
            120,
            1,
            null,
            null,
            6,
            [
                new AutoscalingTargetState(
                    "repo:example/project",
                    "https://github.com/example/project",
                    8,
                    2,
                    1,
                    0,
                    1,
                    0,
                    new ScaleSetStatistics(
                        ObservedAt.AddMinutes(-1),
                        0,
                        0,
                        2,
                        2,
                        1,
                        1,
                        0)),
            ]),
        CapacityEvidence = new ManagerCapacityEvidence(
            null,
            [TargetDeficit()]),
      };

  private static ManagerObservedState CreateFixedProfile() =>
      CreateBaseProfile() with
      {
        ConfiguredSlots = 2,
        CapacityEvidence = new ManagerCapacityEvidence(
            FixedDeficit(),
            []),
      };

  private static ManagerObservedState CreateBaseProfile() =>
      new(
          1,
          12,
          "default",
          "manager-instance",
          "running",
          ObservedAt,
          "repo",
          4,
          new string('a', 64),
          "accepted",
          2,
          1,
          0,
          [
              new ObservedSlotState(
                  "repo-example-000001",
                  "https://github.com/example/project",
                  true,
                  true,
                  "online",
                  0,
                  0,
                  ObservedAt,
                  null,
                  "busy",
                  "repo:example/project",
                  "connected",
                  null,
                  null),
          ],
          null,
          2,
          null,
          1,
          null,
          Journal([Event()]),
          new ManagerSubsystemHealth(
              DegradedSubsystem(),
              HealthySubsystem()),
          null);
}
