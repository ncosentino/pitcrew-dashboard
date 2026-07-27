using Microsoft.Extensions.Options;

using Moq;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class RecoverManagerSyncTests
{
  private readonly MockRepository _mocks = new(MockBehavior.Strict);

  private static readonly DateTimeOffset Now = new(
      2026,
      7,
      26,
      12,
      0,
      0,
      TimeSpan.Zero);

  [Test]
  public async Task Legacy_Connectors_Cannot_Send_Or_Receive_Recovery_Work()
  {
    var recoveryStore = _mocks.Create<IRecoveryCommandStore>();
    var unitOfWork = CreateUnitOfWork(recoveryStore);

    var rejected = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(
            3,
            CreateCapability(),
            null,
            null),
        CancellationToken.None);
    var accepted = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(
            3,
            null,
            null,
            null),
        CancellationToken.None);

    await Assert.That(rejected.Status)
        .IsEqualTo(ConnectorSyncStatus.Invalid);
    await Assert.That(accepted.Status)
        .IsEqualTo(ConnectorSyncStatus.Accepted);
    await Assert.That(accepted.Response!.RecoveryCommand)
        .IsNull()
        .Because("protocol v1-v3 connectors never receive recovery commands");
    recoveryStore.Verify(
        store => store.ApplyConnectorSyncAsync(
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<RecoveryOperatorCapability?>(capability => true),
            It.Is<RecoveryCommandProgress?>(progress => true),
            It.Is<RecoveryCommandOutcome?>(outcome => true),
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<DateTimeOffset>(redeliverBefore => redeliverBefore <= Now),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Version_Four_Connectors_Receive_Claimed_Recovery_Commands()
  {
    var expected = new RecoverManagerCommand(
        Guid.NewGuid(),
        "default",
        "manager-instance",
        4,
        null,
        Now,
        Now.AddMinutes(10));
    var recoveryStore = _mocks.Create<IRecoveryCommandStore>();
    recoveryStore
        .Setup(store => store.ApplyConnectorSyncAsync(
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<RecoveryOperatorCapability?>(capability =>
                capability != null && capability.Profiles.Count == 1),
            It.Is<RecoveryCommandProgress?>(progress =>
                progress != null && progress.Phase == "started"),
            It.Is<RecoveryCommandOutcome?>(outcome => outcome == null),
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<DateTimeOffset>(redeliverBefore => redeliverBefore < Now),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(expected);
    var unitOfWork = CreateUnitOfWork(recoveryStore);

    var result = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(
            4,
            CreateCapability(),
            new RecoveryCommandProgress(
                expected.CommandId,
                "started",
                Now),
            null),
        CancellationToken.None);

    await Assert.That(result.Status)
        .IsEqualTo(ConnectorSyncStatus.Accepted);
    await Assert.That(result.Response!.RecoveryCommand)
        .IsEqualTo(expected);
  }

  [Test]
  public async Task Invalid_Recovery_State_Is_Rejected()
  {
    var recoveryStore = _mocks.Create<IRecoveryCommandStore>();
    var unitOfWork = CreateUnitOfWork(recoveryStore);

    var duplicateProfiles = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(
            4,
            new RecoveryOperatorCapability(
                [
                    CreateCapability().Profiles[0],
                    CreateCapability().Profiles[0],
                ]),
            null,
            null),
        CancellationToken.None);
    var invalidOutcome = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(
            4,
            CreateCapability(),
            null,
            new RecoveryCommandOutcome(
                Guid.NewGuid(),
                "succeeded",
                "timeout",
                null,
                "manager-instance",
                "manager-instance-2",
                Now)),
        CancellationToken.None);

    await Assert.That(duplicateProfiles.Status)
        .IsEqualTo(ConnectorSyncStatus.Invalid);
    await Assert.That(invalidOutcome.Status)
        .IsEqualTo(ConnectorSyncStatus.Invalid);
    recoveryStore.Verify(
        store => store.ApplyConnectorSyncAsync(
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<RecoveryOperatorCapability?>(capability => true),
            It.Is<RecoveryCommandProgress?>(progress => true),
            It.Is<RecoveryCommandOutcome?>(outcome => true),
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<DateTimeOffset>(redeliverBefore => redeliverBefore <= Now),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Recovery_Contract_Validation_Bounds_Evidence_And_Fences()
  {
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidRecoveryOutcome(
                new RecoveryCommandOutcome(
                    Guid.NewGuid(),
                    "indeterminate",
                    "timeout",
                    "The connector lost the execution result.",
                    "manager-instance",
                    null,
                    Now)))
        .IsTrue()
        .Because("indeterminate outcomes carry a bounded failure category");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidRecoveryOutcome(
                new RecoveryCommandOutcome(
                    Guid.NewGuid(),
                    "failed",
                    "not-a-category",
                    null,
                    null,
                    null,
                    Now)))
        .IsFalse()
        .Because("failure categories are bounded");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidRecoveryProgress(
                new RecoveryCommandProgress(
                    Guid.NewGuid(),
                    "finished",
                    Now)))
        .IsFalse()
        .Because("only claimed and started phases exist");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidRecoveryFences(
                "manager-instance",
                4,
                new string('a', 64)))
        .IsTrue();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidRecoveryFences(
                "manager-instance",
                4,
                "not-a-hash"))
        .IsFalse()
        .Because("desired-state hashes are 64 hexadecimal characters");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidRecoveryFences(
                string.Empty,
                4,
                null))
        .IsFalse()
        .Because("an expected manager instance is required");
  }

  private static RecoveryOperatorCapability CreateCapability() =>
      new(
          [
              new RecoveryOperatorProfile(
                  "default",
                  11,
                  true,
                  "manager-instance",
                  4,
                  null,
                  5,
                  true,
                  true,
                  false,
                  600,
                  1800),
          ]);

  private static ConnectorSynchronizationInput CreateInput(
      int protocolVersion,
      RecoveryOperatorCapability? capability,
      RecoveryCommandProgress? progress,
      RecoveryCommandOutcome? outcome) =>
      new(
          protocolVersion,
          "2.0.0",
          Now,
          [],
          null,
          null,
          capability,
          progress,
          outcome);

  private SyncConnectorUnitOfWork CreateUnitOfWork(
      Mock<IRecoveryCommandStore> recoveryStore)
  {
    var fleetStore = _mocks.Create<IFleetStore>();
    fleetStore
        .Setup(store => store.ResolveNodeOrNullAsync(
            It.Is<string>(hash => hash.Length > 0),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ConnectorNodeIdentity(
            Guid.NewGuid(),
            "tenant",
            ConnectorCredentialSlot.Current,
            false));
    fleetStore
        .Setup(store => store.ApplySyncAsync(
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            "2.0.0",
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<IReadOnlyList<ManagerObservedState>>(
                profiles => profiles.Count == 0),
            It.Is<ConnectorCredentialUpdate>(update =>
                update.Kind == ConnectorCredentialUpdateKind.None),
            It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    var capacityStore = _mocks.Create<ICapacityCommandStore>();
    capacityStore
        .Setup(store => store.ApplyConnectorSyncAsync(
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<CapacityOperatorCapability?>(capability => capability == null),
            It.Is<CapacityCommandOutcome?>(outcome => outcome == null),
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<DateTimeOffset>(redeliverBefore => redeliverBefore < Now),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((SetCapacityCommand?)null);
    var historyStore = _mocks.Create<IFleetHistoryStore>();
    historyStore
        .Setup(store => store.AppendAsync(
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<IReadOnlyList<ManagerObservedState>>(
                profiles => profiles.Count == 0),
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<HistoryRetentionPolicy>(
                retention => retention.MaximumSamplesPerProfile > 0),
            It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    return new SyncConnectorUnitOfWork(
        fleetStore.Object,
        historyStore.Object,
        capacityStore.Object,
        recoveryStore.Object,
        new ConnectorCredentialService(),
        Options.Create(new FleetDashboardOptions()),
        new FixedTimeProvider(Now));
  }

  private sealed class FixedTimeProvider(DateTimeOffset _now) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => _now;
  }
}
