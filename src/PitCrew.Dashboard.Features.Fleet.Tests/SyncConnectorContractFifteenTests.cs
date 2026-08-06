using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SyncConnectorContractFifteenTests
{
  [Test]
  public async Task Validates_Bounded_Current_Job_Context()
  {
    var profile = CreateProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile))
        .IsTrue();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              Slots =
              [
                  profile.Slots[0] with
                  {
                    CurrentJob = profile.Slots[0].CurrentJob! with
                    {
                      JobId = "not-a-job-id",
                    },
                  },
              ],
            }))
        .IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              ManagerContractVersion = 14,
            }))
        .IsFalse();
  }

  [Test]
  public async Task Protocol_Eight_Is_Required_For_Job_Context()
  {
    var profile = CreateProfile();

    await Assert.That(
        SyncConnectorUnitOfWork.IsValidProtocolProfileContracts(
            7,
            [profile])).IsFalse();
    await Assert.That(
        SyncConnectorUnitOfWork.IsValidProtocolProfileContracts(
            8,
            [profile])).IsTrue();
  }

  internal static ManagerObservedState CreateProfile()
  {
    var baseline = SyncConnectorContractFourteenTests.CreateProfile();
    return baseline with
    {
      ManagerContractVersion = 15,
      Slots =
      [
          baseline.Slots[0] with
          {
            CurrentJob = new CurrentJobContext(
                "https://github.com/ncosentino/genesis",
                31068390178,
                "92513140749",
                "Android debug build",
                "push",
                baseline.ObservedAt.AddMinutes(-3),
                baseline.ObservedAt.AddMinutes(-2),
                baseline.ObservedAt.AddMinutes(-1),
                baseline.ObservedAt.AddSeconds(-30),
                null,
                null),
          },
      ],
    };
  }
}
