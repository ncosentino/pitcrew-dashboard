using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SyncConnectorContractElevenTests
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
  public async Task IsValidProfile_Accepts_Complete_Contract_Eleven_Projection()
  {
    var profile = CreateContractElevenProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile))
        .IsTrue()
        .Because("a complete contract-11 projection satisfies the contract");
  }

  [Test]
  public async Task IsValidProfile_Accepts_Divergent_Local_And_GitHub_Evidence()
  {
    var profile = CreateContractElevenProfile();
    var autoscaling = RequireAutoscaling(profile);
    var target = autoscaling.Targets![0];
    var registeredWithoutContainers = profile with
    {
      Autoscaling = autoscaling with
      {
        Targets =
        [
            target with
            {
              Statistics = RequireStatistics(target) with
              {
                RegisteredRunners = 8,
                BusyRunners = 2,
                IdleRunners = 6,
              },
            },
        ],
      },
    };
    var containersWithoutRegistrations = profile with
    {
      EligibleSlots = 0,
      Slots =
      [
          profile.Slots[0] with
          {
            RegistrationStatus = "registration-missing",
          },
          profile.Slots[1] with
          {
            RegistrationStatus = "registration-missing",
          },
      ],
      Autoscaling = autoscaling with
      {
        Targets =
        [
            target with
            {
              Statistics = RequireStatistics(target) with
              {
                RegisteredRunners = 0,
                BusyRunners = 0,
                IdleRunners = 0,
              },
            },
        ],
      },
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            registeredWithoutContainers))
        .IsTrue()
        .Because("2 live containers and 8 registered runners is observable divergence, not corruption");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            containersWithoutRegistrations))
        .IsTrue()
        .Because("2 live containers and 0 registered runners is observable divergence, not corruption");
  }

  [Test]
  public async Task IsValidProfile_Accepts_Unavailable_And_Measured_Zero_Evidence()
  {
    var profile = CreateContractElevenProfile();
    var autoscaling = RequireAutoscaling(profile);
    var unavailableEvidence = profile with
    {
      ResourcePolicy = null,
      Autoscaling = autoscaling with
      {
        Targets =
        [
            autoscaling.Targets![0] with
            {
              Statistics = null,
            },
        ],
      },
      Slots =
      [
          profile.Slots[0] with
          {
            ImageId = null,
            LastExit = null,
            Resources = RequireResources(profile.Slots[0]) with
            {
              NetworkRxBytes = null,
              NetworkTxBytes = null,
              BlockReadBytes = null,
              BlockWriteBytes = null,
            },
          },
          profile.Slots[1],
      ],
    };
    var measuredZeroEvidence = profile with
    {
      Slots =
      [
          profile.Slots[0] with
          {
            Resources = RequireResources(profile.Slots[0]) with
            {
              NetworkRxBytes = 0,
              NetworkTxBytes = 0,
              BlockReadBytes = 0,
              BlockWriteBytes = 0,
            },
          },
          profile.Slots[1],
      ],
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            unavailableEvidence))
        .IsTrue()
        .Because("unavailable contract-11 evidence stays null instead of being rejected");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            measuredZeroEvidence))
        .IsTrue()
        .Because("measured-zero counters are valid observations");
  }

  [Test]
  public async Task IsValidProfile_Accepts_Contract_Ten_Payload_Without_Contract_Eleven_Evidence()
  {
    var profile = CreateContractElevenProfile();
    var autoscaling = RequireAutoscaling(profile);
    var contractTenProfile = profile with
    {
      ManagerContractVersion = 10,
      ResourcePolicy = null,
      Autoscaling = autoscaling with
      {
        MaximumActiveWorkers = null,
        Targets = null,
      },
      Slots =
      [
          profile.Slots[0] with
          {
            ImageId = null,
            LastExit = null,
            Resources = new ResourceUsage(
                1.25,
                1_073_741_824,
                48),
          },
          profile.Slots[1] with
          {
            ImageId = null,
            LastExit = null,
            Resources = new ResourceUsage(
                0.5,
                536_870_912,
                24),
          },
      ],
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            contractTenProfile))
        .IsTrue()
        .Because("contract-10 connectors keep synchronizing without contract-11 evidence");
  }

  [Test]
  [Arguments("policy-unconfigured")]
  [Arguments("policy-memory-below-minimum")]
  [Arguments("policy-swap-without-memory")]
  [Arguments("policy-swap-below-memory")]
  [Arguments("policy-cpu-malformed")]
  [Arguments("policy-pids-zero")]
  public async Task IsValidProfile_Rejects_Invalid_Resource_Policy(
      string scenario)
  {
    var profile = CreateContractElevenProfile();
    var policy = profile.ResourcePolicy ??
        throw new InvalidOperationException(
            "The contract-11 fixture must include a resource policy.");
    var invalidProfile = scenario switch
    {
      "policy-unconfigured" => profile with
      {
        ResourcePolicy = new WorkerResourcePolicy(
            null,
            null,
            null,
            null),
      },
      "policy-memory-below-minimum" => profile with
      {
        ResourcePolicy = policy with
        {
          MemoryBytes = 6_291_455,
          MemorySwapBytes = null,
        },
      },
      "policy-swap-without-memory" => profile with
      {
        ResourcePolicy = policy with
        {
          MemoryBytes = null,
        },
      },
      "policy-swap-below-memory" => profile with
      {
        ResourcePolicy = policy with
        {
          MemorySwapBytes = policy.MemoryBytes - 1,
        },
      },
      "policy-cpu-malformed" => profile with
      {
        ResourcePolicy = policy with
        {
          CpuCores = "2.5 cores",
        },
      },
      "policy-pids-zero" => profile with
      {
        ResourcePolicy = policy with
        {
          Pids = 0,
        },
      },
      _ => throw new ArgumentOutOfRangeException(
          nameof(scenario),
          scenario,
          "Unknown resource-policy validation scenario."),
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(invalidProfile))
        .IsFalse()
        .Because($"resource-policy validation must reject '{scenario}'");
  }

  [Test]
  [Arguments("image-id-malformed")]
  [Arguments("io-counter-negative")]
  [Arguments("exit-classification-invalid")]
  [Arguments("exit-evidence-invalid")]
  [Arguments("exit-observed-at-default")]
  [Arguments("exit-code-out-of-range")]
  [Arguments("exit-signal-out-of-range")]
  [Arguments("exit-signal-code-mismatch")]
  [Arguments("exit-oom-without-docker-confirmation")]
  [Arguments("exit-oom-flag-misclassified")]
  [Arguments("exit-clean-with-nonzero-code")]
  [Arguments("exit-error-without-code")]
  [Arguments("exit-unknown-with-code")]
  [Arguments("exit-launch-failure-without-launch-evidence")]
  public async Task IsValidProfile_Rejects_Invalid_Slot_Evidence(
      string scenario)
  {
    var profile = CreateContractElevenProfile();
    var slot = profile.Slots[0];
    var resources = RequireResources(slot);
    var lastExit = profile.Slots[1].LastExit ??
        throw new InvalidOperationException(
            "The contract-11 fixture must include exit evidence.");
    var invalidSlot = scenario switch
    {
      "image-id-malformed" => slot with
      {
        ImageId = "sha256:not-a-digest",
      },
      "io-counter-negative" => slot with
      {
        Resources = resources with
        {
          BlockWriteBytes = -1,
        },
      },
      "exit-classification-invalid" => slot with
      {
        LastExit = lastExit with
        {
          Classification = "crashed",
        },
      },
      "exit-evidence-invalid" => slot with
      {
        LastExit = lastExit with
        {
          Evidence = "guessed",
        },
      },
      "exit-observed-at-default" => slot with
      {
        LastExit = lastExit with
        {
          ObservedAt = default,
        },
      },
      "exit-code-out-of-range" => slot with
      {
        LastExit = lastExit with
        {
          Classification = "error",
          ExitCode = 256,
          Signal = null,
          DockerOomKilled = null,
        },
      },
      "exit-signal-out-of-range" => slot with
      {
        LastExit = lastExit with
        {
          Classification = "signal",
          ExitCode = 193,
          Signal = 65,
          DockerOomKilled = null,
        },
      },
      "exit-signal-code-mismatch" => slot with
      {
        LastExit = lastExit with
        {
          Classification = "sigkill",
          ExitCode = 1,
          Signal = 9,
          DockerOomKilled = null,
        },
      },
      "exit-oom-without-docker-confirmation" => slot with
      {
        LastExit = lastExit with
        {
          Classification = "oom-killed",
          ExitCode = 137,
          Signal = 9,
          DockerOomKilled = null,
        },
      },
      "exit-oom-flag-misclassified" => slot with
      {
        LastExit = lastExit with
        {
          Classification = "sigkill",
          ExitCode = 137,
          Signal = 9,
          DockerOomKilled = true,
        },
      },
      "exit-clean-with-nonzero-code" => slot with
      {
        LastExit = lastExit with
        {
          Classification = "clean",
          ExitCode = 1,
          Signal = null,
          DockerOomKilled = null,
        },
      },
      "exit-error-without-code" => slot with
      {
        LastExit = lastExit with
        {
          Classification = "error",
          ExitCode = null,
          Signal = null,
          DockerOomKilled = null,
        },
      },
      "exit-unknown-with-code" => slot with
      {
        LastExit = lastExit with
        {
          Classification = "unknown",
          Evidence = "unavailable",
          ExitCode = 0,
          Signal = null,
          DockerOomKilled = null,
        },
      },
      "exit-launch-failure-without-launch-evidence" => slot with
      {
        LastExit = lastExit with
        {
          Classification = "launch-failure",
          Evidence = "docker-wait",
          ExitCode = null,
          Signal = null,
          DockerOomKilled = null,
        },
      },
      _ => throw new ArgumentOutOfRangeException(
          nameof(scenario),
          scenario,
          "Unknown slot evidence validation scenario."),
    };

    var invalidProfile = profile with
    {
      Slots =
      [
          invalidSlot,
          profile.Slots[1],
      ],
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(invalidProfile))
        .IsFalse()
        .Because($"slot evidence validation must reject '{scenario}'");
  }

  [Test]
  [Arguments("maximum-active-workers-missing")]
  [Arguments("maximum-active-workers-negative")]
  [Arguments("targets-missing")]
  [Arguments("target-key-blank")]
  [Arguments("target-key-duplicated")]
  [Arguments("target-counter-negative")]
  [Arguments("target-slots-mismatch")]
  [Arguments("target-idle-mismatch")]
  [Arguments("target-busy-mismatch")]
  [Arguments("target-active-over-local-slots")]
  [Arguments("statistics-observed-at-default")]
  [Arguments("statistics-counter-negative")]
  [Arguments("statistics-running-over-assigned")]
  public async Task IsValidProfile_Rejects_Invalid_Target_Projection(
      string scenario)
  {
    var profile = CreateContractElevenProfile();
    var autoscaling = RequireAutoscaling(profile);
    var target = autoscaling.Targets![0];
    var statistics = RequireStatistics(target);
    var invalidAutoscaling = scenario switch
    {
      "maximum-active-workers-missing" => autoscaling with
      {
        MaximumActiveWorkers = null,
      },
      "maximum-active-workers-negative" => autoscaling with
      {
        MaximumActiveWorkers = -1,
      },
      "targets-missing" => autoscaling with
      {
        Targets = null,
      },
      "target-key-blank" => autoscaling with
      {
        Targets =
        [
            target with
            {
              Key = "   ",
            },
        ],
      },
      "target-key-duplicated" => autoscaling with
      {
        IdleRunners = 0,
        BusyRunners = 4,
        TargetSlots = 4,
        Targets =
        [
            target,
            target,
        ],
      },
      "target-counter-negative" => autoscaling with
      {
        Targets =
        [
            target with
            {
              LocalDrainingWorkers = -1,
            },
        ],
      },
      "target-slots-mismatch" => autoscaling with
      {
        Targets =
        [
            target with
            {
              TargetSlots = 1,
            },
        ],
      },
      "target-idle-mismatch" => autoscaling with
      {
        Targets =
        [
            target with
            {
              LocalIdleWorkers = 1,
            },
        ],
      },
      "target-busy-mismatch" => autoscaling with
      {
        Targets =
        [
            target with
            {
              LocalBusyWorkers = 1,
            },
        ],
      },
      "target-active-over-local-slots" => autoscaling with
      {
        Targets =
        [
            target with
            {
              LocalActiveWorkers = 3,
            },
        ],
      },
      "statistics-observed-at-default" => autoscaling with
      {
        Targets =
        [
            target with
            {
              Statistics = statistics with
              {
                ObservedAt = default,
              },
            },
        ],
      },
      "statistics-counter-negative" => autoscaling with
      {
        Targets =
        [
            target with
            {
              Statistics = statistics with
              {
                AcquiredJobs = -1,
              },
            },
        ],
      },
      "statistics-running-over-assigned" => autoscaling with
      {
        Targets =
        [
            target with
            {
              Statistics = statistics with
              {
                AssignedJobs = 1,
                RunningJobs = 2,
              },
            },
        ],
      },
      _ => throw new ArgumentOutOfRangeException(
          nameof(scenario),
          scenario,
          "Unknown target validation scenario."),
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              Autoscaling = invalidAutoscaling,
            }))
        .IsFalse()
        .Because($"target validation must reject '{scenario}'");
  }

  private static ManagerAutoscalingState RequireAutoscaling(
      ManagerObservedState profile) =>
      profile.Autoscaling ??
      throw new InvalidOperationException(
          "The contract-11 fixture must include autoscaling state.");

  private static ScaleSetStatistics RequireStatistics(
      AutoscalingTargetState target) =>
      target.Statistics ??
      throw new InvalidOperationException(
          "The contract-11 fixture must include scale-set statistics.");

  private static ResourceUsage RequireResources(ObservedSlotState slot) =>
      slot.Resources ??
      throw new InvalidOperationException(
          "The contract-11 fixture must include slot resources.");

  private static ManagerObservedState CreateContractElevenProfile() =>
      new(
          1,
          11,
          "default",
          "manager-instance",
          "running",
          ObservedAt,
          "repo",
          4,
          new string('a', 64),
          "accepted",
          2,
          2,
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
                  new ResourceUsage(
                      1.25,
                      1_073_741_824,
                      48,
                      1_048_576,
                      262_144,
                      536_870_912,
                      134_217_728),
                  "busy",
                  "repo:example/project",
                  "connected",
                  "sha256:" + new string('1', 64),
                  null),
              new ObservedSlotState(
                  "repo-example-000002",
                  "https://github.com/example/project",
                  true,
                  true,
                  "online",
                  1,
                  30,
                  ObservedAt,
                  new ResourceUsage(
                      0.5,
                      536_870_912,
                      24,
                      0,
                      0,
                      0,
                      0),
                  "busy",
                  "repo:example/project",
                  "connected",
                  "sha256:" + new string('1', 64),
                  new WorkerLastExitDiagnostic(
                      ObservedAt.AddMinutes(-5),
                      "oom-killed",
                      137,
                      9,
                      true,
                      "docker-inspect")),
          ],
          new ManagerResourceTelemetry(
              ObservedAt,
              "available",
              new HostResourceCapacity(
                  8,
                  34_359_738_368),
              new ResourceUsage(
                  0.5,
                  268_435_456,
                  12)),
          8,
          new ManagerAutoscalingState(
              "scale-set",
              "running",
              0,
              8,
              2,
              2,
              2,
              0,
              0,
              2,
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
                      2,
                      0,
                      2,
                      0,
                      new ScaleSetStatistics(
                          ObservedAt.AddMinutes(-1),
                          0,
                          0,
                          2,
                          2,
                          8,
                          2,
                          6)),
              ]),
          2,
          new WorkerResourcePolicy(
              8_589_934_592,
              10_737_418_240,
              "2.5",
              1024));
}
