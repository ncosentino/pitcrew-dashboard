using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SyncConnectorContractNineteenTests
{
  [Test]
  public async Task Accepts_Complete_Profile_Usable_Capacity()
  {
    var profile = CreateProfile(CreateAccounting(
        allocatableUnits: 4,
        allocatableWorkers: 2,
        theoreticalMaximumUnits: 10,
        theoreticalMaximumWorkers: 5,
        withholdingReason: null));

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile))
        .IsTrue()
        .Because("contract 19 capacity is internally consistent");
  }

  [Test]
  public async Task Accepts_Explicit_Degraded_Compatibility_Evidence()
  {
    var profile = CreateProfile(CreateAccounting(
        allocatableUnits: null,
        allocatableWorkers: null,
        theoreticalMaximumUnits: null,
        theoreticalMaximumWorkers: null,
        withholdingReason: null)) with
    {
      HostAdmission = SyncConnectorContractEighteenTests.CreateProfile()
          .HostAdmission! with
      {
        Status = "degraded",
        Accounting = CreateAccounting(
            allocatableUnits: null,
            allocatableWorkers: null,
            theoreticalMaximumUnits: null,
            theoreticalMaximumWorkers: null,
            withholdingReason: null),
      },
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile))
        .IsTrue()
        .Because("protocol-degraded contract 19 capacity remains explicitly unavailable");
  }

  [Test]
  public async Task Rejects_Missing_Or_Mixed_Contract_Nineteen_Properties()
  {
    var missing = CreateProfile(new HostAdmissionAccounting(
        2,
        4,
        false,
        new string('b', 64),
        2,
        0,
        2,
        0,
        4,
        4));
    var mixed = CreateProfile(CreateAccounting(
        allocatableUnits: 4,
        allocatableWorkers: null,
        theoreticalMaximumUnits: 10,
        theoreticalMaximumWorkers: 5,
        withholdingReason: null));

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(missing))
        .IsFalse()
        .Because("contract 19 requires explicit additive properties");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(mixed))
        .IsFalse()
        .Because("contract 19 capacity numbers are available together");
  }

  [Test]
  [Arguments(4, 3, 10, 5, null)]
  [Arguments(12, 6, 10, 5, null)]
  [Arguments(4, 2, 10, 4, null)]
  [Arguments(4, 2, 10, 5, "budget-exhausted")]
  [Arguments(0, 0, 10, 5, "unsafe/path")]
  public async Task Rejects_Inconsistent_Or_Unsafe_Capacity(
      int allocatableUnits,
      int allocatableWorkers,
      int theoreticalMaximumUnits,
      int theoreticalMaximumWorkers,
      string? withholdingReason)
  {
    var profile = CreateProfile(CreateAccounting(
        allocatableUnits,
        allocatableWorkers,
        theoreticalMaximumUnits,
        theoreticalMaximumWorkers,
        withholdingReason));

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile))
        .IsFalse()
        .Because("worker counts, ceilings, and withholding reasons are producer-owned invariants");
  }

  [Test]
  public async Task Contract_Eighteen_Leaves_Additive_Capacity_Unavailable()
  {
    var legacy = SyncConnectorContractEighteenTests.CreateProfile();
    var mislabeled = legacy with
    {
      HostAdmission = legacy.HostAdmission! with
      {
        Accounting = CreateAccounting(4, 2, 10, 5, null),
      },
    };

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(legacy))
        .IsTrue()
        .Because("contract 18 remains compatible without contract 19 evidence");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(mislabeled))
        .IsFalse()
        .Because("contract 19 authority cannot be mislabeled as contract 18");
  }

  private static ManagerObservedState CreateProfile(
      HostAdmissionAccounting accounting)
  {
    var baseline = SyncConnectorContractEighteenTests.CreateProfile();
    return baseline with
    {
      ManagerContractVersion = 19,
      HostAdmission = baseline.HostAdmission! with
      {
        Accounting = accounting,
      },
    };
  }

  private static HostAdmissionAccounting CreateAccounting(
      int? allocatableUnits,
      int? allocatableWorkers,
      int? theoreticalMaximumUnits,
      int? theoreticalMaximumWorkers,
      string? withholdingReason) =>
      new(
          2,
          4,
          false,
          new string('b', 64),
          2,
          0,
          2,
          0,
          4,
          4,
          allocatableUnits,
          allocatableWorkers,
          theoreticalMaximumUnits,
          theoreticalMaximumWorkers,
          withholdingReason);
}
