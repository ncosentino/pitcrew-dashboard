using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SyncConnectorContractFourteenTests
{
  private const string FirstHash =
      "e0054523055d4ebd049b2b33a1f3b55ba66e5f194b1bbbe5a69eca1ac6a5bf41";
  [Test]
  public async Task IsValidProfile_Accepts_Known_And_Unavailable_Correlation()
  {
    var profile = CreateProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(profile))
        .IsTrue();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              Slots =
              [
                  profile.Slots[0] with { RunnerNameHash = null },
              ],
            }))
        .IsTrue()
        .Because("contract 14 permits unavailable runner identity");
  }

  [Test]
  public async Task IsValidProfile_Rejects_Malformed_And_Duplicate_Hashes()
  {
    var profile = CreateProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              Slots =
              [
                  profile.Slots[0] with
                  {
                    RunnerNameHash = FirstHash.ToUpperInvariant(),
                  },
              ],
            }))
        .IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              Slots =
              [
                  profile.Slots[0],
                  profile.Slots[0] with
                  {
                    Key = "repo-example-000002",
                    ProcessRunning = false,
                    State = "stopped",
                    Activity = null,
                    RegistrationStatus = "disconnected",
                    Resources = null,
                    RunnerNameHash = FirstHash,
                  },
              ],
            }))
        .IsFalse()
        .Because("one hash cannot identify two live slots");
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              Slots =
              [
                  profile.Slots[0] with { Repository = string.Empty },
              ],
            }))
        .IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              Slots =
              [
                  profile.Slots[0] with { Target = string.Empty },
              ],
            }))
        .IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              Slots =
              [
                  profile.Slots[0] with { Target = new string('t', 512) },
              ],
            }))
        .IsTrue();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              Slots =
              [
                  profile.Slots[0] with { Target = new string('t', 513) },
              ],
            }))
        .IsFalse();
  }

  [Test]
  public async Task IsValidProfile_Rejects_Correlation_From_Older_Contracts()
  {
    var profile = CreateProfile();

    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              ManagerContractVersion = 13,
            }))
        .IsFalse();
    await Assert.That(SyncConnectorUnitOfWork.IsValidProfile(
            profile with
            {
              ManagerContractVersion = 13,
              Slots = profile.Slots
                  .Select(slot => slot with
                  {
                    RunnerNameHash = null,
                  })
                  .ToArray(),
            }))
        .IsTrue();
  }

  [Test]
  public async Task Protocol_Seven_Is_Required_For_Manager_Contract_Fourteen()
  {
    var profile = CreateProfile();

    await Assert.That(
        SyncConnectorUnitOfWork.IsValidProtocolProfileContracts(
            6,
            [profile])).IsFalse();
    await Assert.That(
        SyncConnectorUnitOfWork.IsValidProtocolProfileContracts(
            7,
            [profile])).IsTrue();
    await Assert.That(
        SyncConnectorUnitOfWork.IsValidProtocolProfileContracts(
            6,
            [
                profile with
                {
                  ManagerContractVersion = 13,
                  Slots = profile.Slots
                      .Select(slot => slot with
                      {
                        RunnerNameHash = null,
                      })
                      .ToArray(),
                },
            ])).IsTrue();
  }

  internal static ManagerObservedState CreateProfile()
  {
    var baseline =
        SyncConnectorContractTwelveTests.CreateAutoscaledProfile();
    return baseline with
    {
      ManagerContractVersion = 14,
      Host = new ObservedHost(
          SyncConnectorContractThirteenTests.CreateHardware(
              baseline.ObservedAt)),
      Slots =
      [
          baseline.Slots[0] with
          {
            RunnerNameHash = FirstHash,
          },
      ],
    };
  }
}
