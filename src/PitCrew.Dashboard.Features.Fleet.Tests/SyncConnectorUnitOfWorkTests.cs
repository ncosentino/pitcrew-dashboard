using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SyncConnectorUnitOfWorkTests
{
  [Test]
  public async Task IsValidProfile_Accepts_Compatible_Resource_Telemetry()
  {
    var sampledAt = new DateTimeOffset(
        2026,
        7,
        19,
        18,
        30,
        0,
        TimeSpan.Zero);
    var legacyProfile = CreateProfile(
        sampledAt,
        null,
        null);
    var partialProfile = CreateProfile(
        sampledAt,
        new ManagerResourceTelemetry(
            sampledAt,
            "partial",
            new HostResourceCapacity(
                8,
                34_359_738_368),
            null),
        null);
    var unavailableProfile = CreateProfile(
        sampledAt,
        new ManagerResourceTelemetry(
            sampledAt,
            "unavailable",
            null,
            null),
        null);
    var uncappedCpuProfile = CreateProfile(
        sampledAt,
        new ManagerResourceTelemetry(
            sampledAt,
            "available",
            new HostResourceCapacity(
                8,
                34_359_738_368),
            new ResourceUsage(
                32.5,
                268_435_456,
                12)),
        new ResourceUsage(
            12.25,
            1_073_741_824,
            48));

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            legacyProfile))
        .IsTrue()
        .Because("missing resource blocks are valid during rolling upgrades");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            partialProfile))
        .IsTrue()
        .Because("partial telemetry may omit manager usage");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            unavailableProfile))
        .IsTrue()
        .Because("unavailable telemetry may omit all resource blocks");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            uncappedCpuProfile))
        .IsTrue()
        .Because("CPU usage is expressed as cores and has no percentage cap");
  }

  [Test]
  [Arguments("manager-cpu-not-a-number")]
  [Arguments("manager-cpu-infinite")]
  [Arguments("manager-cpu-negative")]
  [Arguments("manager-memory-negative")]
  [Arguments("manager-pids-negative")]
  [Arguments("slot-cpu-not-a-number")]
  [Arguments("slot-memory-negative")]
  [Arguments("host-logical-processors-zero")]
  [Arguments("host-memory-zero")]
  [Arguments("sampled-at-default")]
  [Arguments("status-invalid")]
  [Arguments("telemetry-missing-with-slot-resources")]
  [Arguments("available-host-missing")]
  [Arguments("available-manager-missing")]
  [Arguments("partial-empty")]
  [Arguments("unavailable-manager-present")]
  [Arguments("unavailable-slot-present")]
  public async Task IsValidProfile_Rejects_Invalid_Resource_Telemetry(
      string scenario)
  {
    var sampledAt = new DateTimeOffset(
        2026,
        7,
        19,
        18,
        30,
        0,
        TimeSpan.Zero);
    var validManager = new ResourceUsage(
        0.5,
        268_435_456,
        12);
    var validSlot = new ResourceUsage(
        1.25,
        1_073_741_824,
        48);
    var validHost = new HostResourceCapacity(
        8,
        34_359_738_368);
    var validTelemetry = new ManagerResourceTelemetry(
        sampledAt,
        "available",
        validHost,
        validManager);
    var profile = CreateProfile(
        sampledAt,
        validTelemetry,
        validSlot);
    var invalidProfile = scenario switch
    {
      "manager-cpu-not-a-number" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Manager = validManager with
          {
            CpuCores = double.NaN,
          },
        },
      },
      "manager-cpu-infinite" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Manager = validManager with
          {
            CpuCores = double.PositiveInfinity,
          },
        },
      },
      "manager-cpu-negative" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Manager = validManager with
          {
            CpuCores = -0.01,
          },
        },
      },
      "manager-memory-negative" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Manager = validManager with
          {
            MemoryWorkingSetBytes = -1,
          },
        },
      },
      "manager-pids-negative" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Manager = validManager with
          {
            Pids = -1,
          },
        },
      },
      "slot-cpu-not-a-number" => profile with
      {
        Slots =
        [
            profile.Slots[0] with
            {
              Resources = validSlot with
              {
                CpuCores = double.NaN,
              },
            },
        ],
      },
      "slot-memory-negative" => profile with
      {
        Slots =
        [
            profile.Slots[0] with
            {
              Resources = validSlot with
              {
                MemoryWorkingSetBytes = -1,
              },
            },
        ],
      },
      "host-logical-processors-zero" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Host = validHost with
          {
            LogicalProcessorCount = 0,
          },
        },
      },
      "host-memory-zero" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Host = validHost with
          {
            MemoryBytes = 0,
          },
        },
      },
      "sampled-at-default" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          SampledAt = default,
        },
      },
      "status-invalid" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Status = "unknown",
        },
      },
      "telemetry-missing-with-slot-resources" => profile with
      {
        ResourceTelemetry = null,
      },
      "available-host-missing" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Host = null,
        },
      },
      "available-manager-missing" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Manager = null,
        },
      },
      "partial-empty" => profile with
      {
        ResourceTelemetry = new ManagerResourceTelemetry(
            sampledAt,
            "partial",
            null,
            null),
        Slots =
        [
            profile.Slots[0] with
            {
              Resources = null,
            },
        ],
      },
      "unavailable-manager-present" => profile with
      {
        ResourceTelemetry = validTelemetry with
        {
          Status = "unavailable",
          Host = null,
        },
        Slots =
        [
            profile.Slots[0] with
            {
              Resources = null,
            },
        ],
      },
      "unavailable-slot-present" => profile with
      {
        ResourceTelemetry = new ManagerResourceTelemetry(
            sampledAt,
            "unavailable",
            null,
            null),
      },
      _ => throw new ArgumentOutOfRangeException(
          nameof(scenario),
          scenario,
          "Unknown resource validation scenario."),
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            invalidProfile))
        .IsFalse()
        .Because($"resource validation must reject '{scenario}'");
  }

  [Test]
  public async Task IsValidProfile_Accepts_Rolling_And_Complete_Autoscaling_States()
  {
    var observedAt = new DateTimeOffset(
        2026,
        7,
        20,
        12,
        0,
        0,
        TimeSpan.Zero);
    var strippedContractEightProfile = CreateProfile(
        observedAt,
        null,
        null) with
    {
      ManagerContractVersion = 8,
    };
    var fixedProfile = strippedContractEightProfile with
    {
      ConfiguredSlots = 1,
    };
    var autoscaledProfileWithoutConfiguredSlots =
        strippedContractEightProfile with
        {
          Autoscaling = CreateAutoscalingState(observedAt),
        };
    var autoscaledProfile = strippedContractEightProfile with
    {
      ConfiguredSlots = 30,
      Autoscaling = CreateAutoscalingState(observedAt) with
      {
        ScaleDownAt = null,
      },
      Slots =
      [
          strippedContractEightProfile.Slots[0] with
          {
            Activity = "busy",
            Target = "scale-set-linux",
          },
      ],
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            strippedContractEightProfile))
        .IsTrue()
        .Because("contract 8 remains valid when an older connector strips every additive field");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            fixedProfile))
        .IsTrue()
        .Because("fixed mode may report configured capacity without an autoscaling block");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            autoscaledProfileWithoutConfiguredSlots))
        .IsTrue()
        .Because("maximum capacity is compared only when configured capacity survived synchronization");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            autoscaledProfile))
        .IsTrue()
        .Because("a consistent demand-driven observation may omit its scale-down timestamp");
  }

  [Test]
  [Arguments("starting", "starting")]
  [Arguments("running", "idle")]
  [Arguments("running", "busy")]
  [Arguments("degraded", "draining")]
  [Arguments("stopping", "unknown")]
  public async Task IsValidProfile_Accepts_Supported_Autoscaling_Enums(
      string status,
      string activity)
  {
    var observedAt = new DateTimeOffset(
        2026,
        7,
        20,
        12,
        0,
        0,
        TimeSpan.Zero);
    var profile = CreateProfile(
        observedAt,
        null,
        null);
    var autoscaledProfile = profile with
    {
      ManagerContractVersion = 8,
      ConfiguredSlots = 30,
      Autoscaling = CreateAutoscalingState(observedAt) with
      {
        Status = status,
      },
      Slots =
      [
          profile.Slots[0] with
          {
            Activity = activity,
            Target = "scale-set-linux",
          },
      ],
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            autoscaledProfile))
        .IsTrue()
        .Because($"status '{status}' and activity '{activity}' are supported contract values");
  }

  [Test]
  [Arguments("configured-negative")]
  [Arguments("activity-invalid")]
  [Arguments("mode-invalid")]
  [Arguments("status-invalid")]
  [Arguments("minimum-idle-negative")]
  [Arguments("maximum-negative")]
  [Arguments("target-negative")]
  [Arguments("assigned-negative")]
  [Arguments("running-negative")]
  [Arguments("available-negative")]
  [Arguments("idle-negative")]
  [Arguments("busy-negative")]
  [Arguments("scale-down-delay-negative")]
  [Arguments("scale-set-count-negative")]
  [Arguments("scale-down-at-default")]
  [Arguments("maximum-configured-mismatch")]
  [Arguments("desired-target-mismatch")]
  [Arguments("target-over-maximum")]
  [Arguments("running-over-assigned")]
  [Arguments("busy-over-active")]
  [Arguments("runner-total-over-active")]
  public async Task IsValidProfile_Rejects_Invalid_Autoscaling_State(
      string scenario)
  {
    var observedAt = new DateTimeOffset(
        2026,
        7,
        20,
        12,
        0,
        0,
        TimeSpan.Zero);
    var profile = CreateProfile(
        observedAt,
        null,
        null) with
    {
      ManagerContractVersion = 8,
      ConfiguredSlots = 30,
      Autoscaling = CreateAutoscalingState(observedAt),
    };
    var autoscaling = profile.Autoscaling ??
        throw new InvalidOperationException(
            "The autoscaling test fixture must include autoscaling state.");
    var invalidProfile = scenario switch
    {
      "configured-negative" => profile with
      {
        ConfiguredSlots = -1,
      },
      "activity-invalid" => profile with
      {
        Slots =
        [
            profile.Slots[0] with
            {
              Activity = "sleeping",
            },
        ],
      },
      "mode-invalid" => profile with
      {
        Autoscaling = autoscaling with
        {
          Mode = "fixed",
        },
      },
      "status-invalid" => profile with
      {
        Autoscaling = autoscaling with
        {
          Status = "stopped",
        },
      },
      "minimum-idle-negative" => profile with
      {
        Autoscaling = autoscaling with
        {
          MinimumIdleSlots = -1,
        },
      },
      "maximum-negative" => profile with
      {
        ConfiguredSlots = null,
        Autoscaling = autoscaling with
        {
          MaximumSlots = -1,
        },
      },
      "target-negative" => profile with
      {
        Autoscaling = autoscaling with
        {
          TargetSlots = -1,
        },
      },
      "assigned-negative" => profile with
      {
        Autoscaling = autoscaling with
        {
          AssignedJobs = -1,
        },
      },
      "running-negative" => profile with
      {
        Autoscaling = autoscaling with
        {
          RunningJobs = -1,
        },
      },
      "available-negative" => profile with
      {
        Autoscaling = autoscaling with
        {
          AvailableJobs = -1,
        },
      },
      "idle-negative" => profile with
      {
        Autoscaling = autoscaling with
        {
          IdleRunners = -1,
        },
      },
      "busy-negative" => profile with
      {
        Autoscaling = autoscaling with
        {
          BusyRunners = -1,
        },
      },
      "scale-down-delay-negative" => profile with
      {
        Autoscaling = autoscaling with
        {
          ScaleDownDelaySeconds = -1,
        },
      },
      "scale-set-count-negative" => profile with
      {
        Autoscaling = autoscaling with
        {
          ScaleSetCount = -1,
        },
      },
      "scale-down-at-default" => profile with
      {
        Autoscaling = autoscaling with
        {
          ScaleDownAt = default(DateTimeOffset),
        },
      },
      "maximum-configured-mismatch" => profile with
      {
        ConfiguredSlots = 29,
      },
      "desired-target-mismatch" => profile with
      {
        DesiredSlots = 2,
      },
      "target-over-maximum" => profile with
      {
        DesiredSlots = 31,
        Autoscaling = autoscaling with
        {
          TargetSlots = 31,
        },
      },
      "running-over-assigned" => profile with
      {
        Autoscaling = autoscaling with
        {
          AssignedJobs = 0,
        },
      },
      "busy-over-active" => profile with
      {
        Autoscaling = autoscaling with
        {
          BusyRunners = 2,
        },
      },
      "runner-total-over-active" => profile with
      {
        Autoscaling = autoscaling with
        {
          IdleRunners = 1,
        },
      },
      _ => throw new ArgumentOutOfRangeException(
          nameof(scenario),
          scenario,
          "Unknown autoscaling validation scenario."),
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            invalidProfile))
        .IsFalse()
        .Because($"autoscaling validation must reject '{scenario}'");
  }

  private static ManagerObservedState CreateProfile(
      DateTimeOffset sampledAt,
      ManagerResourceTelemetry? resourceTelemetry,
      ResourceUsage? slotResources) =>
      new(
          1,
          7,
          "default",
          "manager-instance",
          "running",
          sampledAt,
          "repo",
          1,
          new string('a', 64),
          "accepted",
          1,
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
                  sampledAt,
                  slotResources,
                  null,
                  null),
          ],
          resourceTelemetry,
          null,
          null);

  private static ManagerAutoscalingState CreateAutoscalingState(
      DateTimeOffset observedAt) =>
      new(
          "scale-set",
          "running",
          0,
          30,
          1,
          1,
          1,
          0,
          0,
          1,
          300,
          1,
          observedAt.AddMinutes(5),
          null);
}
