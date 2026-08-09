using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SyncConnectorContractEighteenTests
{
  [Test]
  public async Task Requires_Complete_Host_Admission_From_Contract_Eighteen()
  {
    var profile = CreateProfile();
    var admission = profile.HostAdmission ??
        throw new InvalidOperationException(
            "The contract-18 fixture must include host admission.");

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile))
        .IsTrue()
        .Because("complete contract-18 host admission is valid");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile with
    {
      HostAdmission = null,
    })).IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile with
    {
      HostAdmission = admission with
      {
        Accounting = admission.Accounting! with
        {
          PendingUnits = null,
          WithheldUnits = null,
        },
      },
    })).IsFalse();
  }

  [Test]
  public async Task Accepts_Explicit_Unavailable_And_Legacy_Missing_Evidence()
  {
    var profile = CreateProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile with
    {
      HostAdmission = new HostAdmissionState(
          "unavailable",
          "primary",
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null,
          null),
    })).IsTrue();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile with
    {
      ManagerContractVersion = 17,
      HostAdmission = null,
    })).IsTrue();
  }

  [Test]
  public async Task Rejects_Inconsistent_Accounting_And_Unsafe_Decision_Codes()
  {
    var profile = CreateProfile();
    var admission = profile.HostAdmission ??
        throw new InvalidOperationException(
            "The contract-18 fixture must include host admission.");

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile with
    {
      HostAdmission = admission with
      {
        Accounting = admission.Accounting! with
        {
          HeldUnits = 99,
        },
      },
    })).IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile with
    {
      HostAdmission = admission with
      {
        LastDecision = admission.LastDecision! with
        {
          FailureCategory = "/var/lib/private",
        },
      },
    })).IsFalse();
  }

  private static ManagerObservedState CreateProfile()
  {
    var baseline = SyncConnectorContractSixteenTests.CreateProfile();
    return baseline with
    {
      ManagerContractVersion = 18,
      HostAdmission = new HostAdmissionState(
          "available",
          "primary",
          3,
          42,
          12,
          2,
          10,
          4,
          new string('a', 64),
          new HostAdmissionAccounting(
              2,
              4,
              false,
              new string('b', 64),
              5,
              0,
              5,
              1,
              4,
              4),
          new HostAdmissionDecision(
              42,
              "acquire",
              false,
              "budget-exceeded",
              1_754_719_500_000_000_000)),
    };
  }
}
