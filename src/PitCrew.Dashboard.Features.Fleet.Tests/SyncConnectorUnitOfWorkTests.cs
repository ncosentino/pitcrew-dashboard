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
                  slotResources),
          ],
          resourceTelemetry);
}
