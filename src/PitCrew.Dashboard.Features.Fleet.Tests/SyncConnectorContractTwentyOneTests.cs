using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class SyncConnectorContractTwentyOneTests
{
  [Test]
  public async Task Protocol_Twelve_Requires_Explicit_Profile_Inventory()
  {
    var missing = SyncConnectorUnitOfWork.IsValidProfileInventory(
        12,
        null,
        0);
    var unavailable = SyncConnectorUnitOfWork.IsValidProfileInventory(
        12,
        new ConnectorProfileInventory(
            "unavailable",
            new DateTimeOffset(
                2026,
                8,
                20,
                1,
                0,
                0,
                TimeSpan.Zero),
            "state-root-missing"),
        0);

    await Assert.That(missing).IsFalse()
        .Because("protocol 12 cannot silently downgrade unknown inventory to an empty list");
    await Assert.That(unavailable).IsTrue()
        .Because("protocol 12 represents total acquisition loss explicitly");
  }

  [Test]
  public async Task Partial_Inventory_Requires_At_Least_One_Observed_Profile()
  {
    var observedAt = new DateTimeOffset(
        2026,
        8,
        20,
        1,
        0,
        0,
        TimeSpan.Zero);
    var emptyPartial = SyncConnectorUnitOfWork.IsValidProfileInventory(
        12,
        new ConnectorProfileInventory(
            "partial",
            observedAt,
            "profile-state-unreadable"),
        0);
    var nonEmptyPartial = SyncConnectorUnitOfWork.IsValidProfileInventory(
        12,
        new ConnectorProfileInventory(
            "partial",
            observedAt,
            "profile-state-unreadable"),
        1);

    await Assert.That(emptyPartial).IsFalse()
        .Because("partial coverage requires at least one usable profile");
    await Assert.That(nonEmptyPartial).IsTrue()
        .Because("partial coverage preserves successfully observed profiles");
  }

  [Test]
  public async Task Manager_Contract_Twenty_One_Requires_Protocol_Twelve()
  {
    var profile = new ManagerObservedState(
        1,
        21,
        "default",
        "manager-instance",
        "running",
        new DateTimeOffset(
            2026,
            8,
            20,
            1,
            0,
            0,
            TimeSpan.Zero),
        "repo",
        1,
        null,
        "accepted",
        0,
        0,
        0,
        [],
        null,
        0,
        null);

    var oldProtocol =
        SyncConnectorUnitOfWork.IsValidProtocolProfileContracts(
            11,
            [profile]);
    var currentProtocol =
        SyncConnectorUnitOfWork.IsValidProtocolProfileContracts(
            12,
            [profile]);

    await Assert.That(oldProtocol).IsFalse()
        .Because("an older receiver cannot preserve contract-21 inventory semantics");
    await Assert.That(currentProtocol).IsTrue()
        .Because("protocol 12 carries the required additive evidence envelope");
  }
}
