using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SyncConnectorContractThirteenTests
{
  [Test]
  public async Task IsValidProfile_Accepts_Current_Stale_And_Unavailable_Hardware()
  {
    var baseline =
        SyncConnectorContractTwelveTests.CreateAutoscaledProfile();
    var current = baseline with
    {
      ManagerContractVersion = 13,
      Host = new ObservedHost(CreateHardware(baseline.ObservedAt)),
    };
    var stale = current with
    {
      Host = new ObservedHost(
          CreateHardware(baseline.ObservedAt) with
          {
            Status = "stale",
          }),
    };
    var unavailable = current with
    {
      Host = new ObservedHost(new HostHardwareInventory(
          "unavailable",
          null,
          baseline.ObservedAt,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null)),
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(current))
        .IsTrue();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(stale))
        .IsTrue();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(unavailable))
        .IsTrue();
    await Assert.That(IsValid(
            current,
            CreateHardware(baseline.ObservedAt) with
            {
              InventoryHash =
                  "34d718afe041d1d07eff83c4db34cb913bbb7bd71537d0091160486aba23ce89",
              Architecture = "riscv64",
            }))
        .IsTrue();
  }

  [Test]
  public async Task IsValidProfile_Requires_Hardware_Only_From_Contract_Thirteen()
  {
    var baseline =
        SyncConnectorContractTwelveTests.CreateAutoscaledProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            baseline with
            {
              ManagerContractVersion = 13,
              Host = null,
            }))
        .IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            baseline with
            {
              ManagerContractVersion = 12,
              Host = null,
            }))
        .IsTrue();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            baseline with
            {
              ManagerContractVersion = 12,
              Host = new ObservedHost(
                  CreateHardware(baseline.ObservedAt)),
            }))
        .IsFalse()
        .Because("older contracts cannot smuggle contract-13 hardware");
  }

  [Test]
  public async Task IsValidProfile_Rejects_Malformed_Hardware()
  {
    var baseline =
        SyncConnectorContractTwelveTests.CreateAutoscaledProfile();
    var hardware = CreateHardware(baseline.ObservedAt);
    var profile = baseline with
    {
      ManagerContractVersion = 13,
      Host = new ObservedHost(hardware),
    };

    await Assert.That(IsValid(
            profile,
            hardware with { Status = "healthy" }))
        .IsFalse();
    await Assert.That(IsValid(
            profile,
            hardware with
            {
              Architecture = new string('a', 65),
            }))
        .IsFalse();
    await Assert.That(IsValid(
            profile,
            hardware with { PhysicalCoreCount = 0 }))
        .IsFalse();
    await Assert.That(IsValid(
            profile,
            hardware with
            {
              ProcessorModel = "line\nbreak",
            }))
        .IsFalse();
    await Assert.That(IsValid(
            profile,
            hardware with
            {
              AttemptedAt = baseline.ObservedAt.AddSeconds(1),
            }))
        .IsFalse();
    await Assert.That(IsValid(
            profile,
            hardware with
            {
              CollectedAt = hardware.AttemptedAt.AddSeconds(1),
            }))
        .IsFalse();
    await Assert.That(IsValid(
            profile,
            hardware with
            {
              InventoryHash = new string('0', 64),
            }))
        .IsFalse();
    await Assert.That(IsValid(
            profile,
            hardware with
            {
              Status = "unavailable",
            }))
        .IsFalse()
        .Because("unavailable hardware cannot retain measured values");
  }

  private static bool IsValid(
      ManagerObservedState profile,
      HostHardwareInventory hardware) =>
      SyncConnectorUnitOfWork.IsValidProfile(profile with
      {
        Host = new ObservedHost(hardware),
      });

  private static HostHardwareInventory CreateHardware(
      DateTimeOffset observedAt) =>
      new(
          "current",
          observedAt.AddMinutes(-5),
          observedAt,
          "c4e642cd75f1f5b5028b528beefca104d35f7eccc3dac31627017d4ed5857e42",
          "Example Processor 9000",
          "amd64",
          2,
          16,
          null,
          null,
          34359738368,
          "Docker Desktop",
          "6.12.34-test",
          "28.3.3",
          "overlayfs",
          "extfs");
}
