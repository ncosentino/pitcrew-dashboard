using Microsoft.Extensions.Options;

using Moq;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class RollOutProfileImageSyncTests
{
  private readonly MockRepository _mocks = new(MockBehavior.Strict);

  private static readonly DateTimeOffset Now = new(
      2026,
      8,
      1,
      12,
      0,
      0,
      TimeSpan.Zero);

  private const string StaticFingerprint =
      "a1b2c3d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff0";
  private const string PreservedFingerprint =
      "b2c3d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001";
  private const string RoutingFingerprint =
      "c3d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff00112";
  private const string DesiredStateHash =
      "e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001a1b2c3";
  private const string CurrentWorkerRevision =
      "d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001a1b2";
  private const string TargetDigest =
      "sha256:0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
  private const string CurrentImageDigest =
      "sha256:1111111111111111111111111111111111111111111111111111111111111111";
  private const string CurrentLocalImageId =
      "sha256:2222222222222222222222222222222222222222222222222222222222222222";

  [Test]
  public async Task Protocol_Versions_Below_Eleven_Cannot_Send_Rollout_Fields()
  {
    var rolloutStore = _mocks.Create<IImageRolloutCommandStore>();
    var unitOfWork = CreateUnitOfWork(rolloutStore);

    var withCapability = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(10, CreateCapability(), null, null),
        CancellationToken.None);
    var withProgress = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(10, null, CreateProgress(), null),
        CancellationToken.None);
    var withOutcome = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(10, null, null, CreateSuccessOutcome()),
        CancellationToken.None);
    var legacyClean = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(10, null, null, null),
        CancellationToken.None);

    await Assert.That(withCapability.Status)
        .IsEqualTo(ConnectorSyncStatus.Invalid);
    await Assert.That(withProgress.Status)
        .IsEqualTo(ConnectorSyncStatus.Invalid);
    await Assert.That(withOutcome.Status)
        .IsEqualTo(ConnectorSyncStatus.Invalid);
    await Assert.That(legacyClean.Status)
        .IsEqualTo(ConnectorSyncStatus.Accepted);
    await Assert.That(legacyClean.Response!.ImageRolloutCommand)
        .IsNull()
        .Because(
            "protocol v1-v10 connectors never receive rollout commands");
    rolloutStore.Verify(
        store => store.ApplyConnectorSyncAsync(
            It.Is<Guid>(_ => true),
            It.Is<ImageRolloutOperatorCapability?>(_ => true),
            It.Is<ImageRolloutCommandProgress?>(_ => true),
            It.Is<ImageRolloutCommandOutcome?>(_ => true),
            It.Is<DateTimeOffset>(_ => true),
            It.Is<DateTimeOffset>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Version_Eleven_Connectors_Receive_Rollout_Commands()
  {
    var expected = new RollOutProfileImageCommand(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "copilot-cli",
        "default",
        TargetDigest,
        "linux/amd64",
        "ghcr.io/example/runner:main",
        CurrentImageDigest,
        CurrentLocalImageId,
        CurrentWorkerRevision,
        StaticFingerprint,
        PreservedFingerprint,
        RoutingFingerprint,
        7,
        DesiredStateHash,
        Now,
        Now.AddMinutes(10));
    var rolloutStore = _mocks.Create<IImageRolloutCommandStore>();
    rolloutStore
        .Setup(store => store.ApplyConnectorSyncAsync(
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<ImageRolloutOperatorCapability?>(capability =>
                capability != null && capability.Profiles.Count == 1),
            It.Is<ImageRolloutCommandProgress?>(progress =>
                progress != null && progress.Phase == "claimed"),
            It.Is<ImageRolloutCommandOutcome?>(outcome => outcome == null),
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<DateTimeOffset>(redeliverBefore => redeliverBefore < Now),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(expected);
    var unitOfWork = CreateUnitOfWork(rolloutStore);

    var result = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(
            11,
            CreateCapability(),
            new ImageRolloutCommandProgress(
                expected.CommandId,
                "claimed",
                Now),
            null),
        CancellationToken.None);

    await Assert.That(result.Status)
        .IsEqualTo(ConnectorSyncStatus.Accepted);
    await Assert.That(result.Response!.ImageRolloutCommand)
        .IsEqualTo(expected);
  }

  [Test]
  public async Task Invalid_Rollout_State_Is_Rejected_Before_Storage()
  {
    var rolloutStore = _mocks.Create<IImageRolloutCommandStore>();
    var unitOfWork = CreateUnitOfWork(rolloutStore);

    var duplicateProfiles = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(
            11,
            new ImageRolloutOperatorCapability(
                [
                    CreateCapability().Profiles[0],
                    CreateCapability().Profiles[0],
                ]),
            null,
            null),
        CancellationToken.None);
    var invalidPhase = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(
            11,
            CreateCapability(),
            new ImageRolloutCommandProgress(
                Guid.NewGuid(),
                "finished",
                Now),
            null),
        CancellationToken.None);
    var succeededWithFailureCategory = await unitOfWork.SynchronizeAsync(
        "credential",
        CreateInput(
            11,
            CreateCapability(),
            null,
            new ImageRolloutCommandOutcome(
                Guid.NewGuid(),
                "succeeded",
                "timeout",
                null,
                TargetDigest,
                CurrentWorkerRevision,
                "current",
                4,
                0,
                null,
                Now)),
        CancellationToken.None);

    await Assert.That(duplicateProfiles.Status)
        .IsEqualTo(ConnectorSyncStatus.Invalid);
    await Assert.That(invalidPhase.Status)
        .IsEqualTo(ConnectorSyncStatus.Invalid);
    await Assert.That(succeededWithFailureCategory.Status)
        .IsEqualTo(ConnectorSyncStatus.Invalid);
    rolloutStore.Verify(
        store => store.ApplyConnectorSyncAsync(
            It.Is<Guid>(_ => true),
            It.Is<ImageRolloutOperatorCapability?>(_ => true),
            It.Is<ImageRolloutCommandProgress?>(_ => true),
            It.Is<ImageRolloutCommandOutcome?>(_ => true),
            It.Is<DateTimeOffset>(_ => true),
            It.Is<DateTimeOffset>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Rollout_Contract_Validation_Bounds_Capability_Fences_And_Outcomes()
  {
    var capability = CreateCapability();

    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOperator(capability))
        .IsTrue();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOperator(
                new ImageRolloutOperatorCapability(
                    [
                        capability.Profiles[0] with
                        {
                            Architecture = "windows/amd64",
                        },
                    ])))
        .IsFalse()
        .Because("only linux/amd64 and linux/arm64 are advertised");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOperator(
                new ImageRolloutOperatorCapability(
                    [
                        capability.Profiles[0] with
                        {
                            StaticFingerprint = "not-a-hash",
                        },
                    ])))
        .IsFalse()
        .Because("fingerprints are 64 hex characters");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOperator(
                new ImageRolloutOperatorCapability(
                    [
                        capability.Profiles[0] with
                        {
                            LocalFailureCategory = "unknown-category",
                        },
                    ])))
        .IsFalse()
        .Because("failure categories are bounded");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOperator(
                new ImageRolloutOperatorCapability(
                    [
                        capability.Profiles[0] with
                        {
                            RolloutAllowed = false,
                            LocalFailureCategory = "stale-observed-state",
                            ObservedStateAgeSeconds = 86_400,
                        },
                    ])))
        .IsTrue()
        .Because(
            "stale-observed-state is an accepted capability category and "
            + "86_400 is the bounded wire sentinel emitted by the connector");
    // Distinct wire vocabulary for capability LocalFailureCategory must
    // accept every closed category we now emit at the connector
    // capability boundary: unsupported-topology and unsupported-architecture
    // are distinct from schema/manager unsupported.
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOperator(
                new ImageRolloutOperatorCapability(
                    [
                        capability.Profiles[0] with
                        {
                            RolloutAllowed = false,
                            LocalFailureCategory = "unsupported-topology",
                        },
                    ])))
        .IsTrue()
        .Because(
            "unsupported-topology is a distinct closed capability category "
            + "for routing-projection failures");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOperator(
                new ImageRolloutOperatorCapability(
                    [
                        capability.Profiles[0] with
                        {
                            RolloutAllowed = false,
                            LocalFailureCategory = "recipe-not-allowed",
                        },
                    ])))
        .IsTrue()
        .Because(
            "recipe-not-allowed distinguishes recipe-scoped rejection "
            + "from a general profile-scoped not-allowed");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOperator(
                new ImageRolloutOperatorCapability(
                    [
                        capability.Profiles[0] with
                        {
                            RolloutAllowed = false,
                            LocalFailureCategory = "registry-not-allowed",
                        },
                    ])))
        .IsTrue()
        .Because(
            "registry-not-allowed distinguishes local registry-policy "
            + "rejection from schema/manager or recipe rejection");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOperator(
                new ImageRolloutOperatorCapability(
                    [
                        capability.Profiles[0] with
                        {
                            ManagerConvergenceStatus = "converged",
                        },
                    ])))
        .IsFalse()
        .Because("convergence status uses current/rolling/degraded");

    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutFences(
                new ImageRolloutCommandFences(
                    "ghcr.io/example/runner:main",
                    CurrentImageDigest,
                    CurrentLocalImageId,
                    CurrentWorkerRevision,
                    StaticFingerprint,
                    PreservedFingerprint,
                    RoutingFingerprint,
                    7,
                    DesiredStateHash)))
        .IsTrue();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutFences(
                new ImageRolloutCommandFences(
                    null,
                    null,
                    null,
                    null,
                    StaticFingerprint,
                    PreservedFingerprint,
                    RoutingFingerprint,
                    7,
                    null)))
        .IsTrue()
        .Because("prior-evidence fences are optional");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutFences(
                new ImageRolloutCommandFences(
                    null,
                    "sha256:notreallyhexadecimal",
                    null,
                    null,
                    StaticFingerprint,
                    PreservedFingerprint,
                    RoutingFingerprint,
                    7,
                    DesiredStateHash)))
        .IsFalse()
        .Because("digests must be 71-character lowercase sha256 hex");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutFences(
                new ImageRolloutCommandFences(
                    null,
                    null,
                    null,
                    null,
                    "not-a-hash",
                    PreservedFingerprint,
                    RoutingFingerprint,
                    7,
                    null)))
        .IsFalse();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutFences(
                new ImageRolloutCommandFences(
                    null,
                    null,
                    null,
                    null,
                    StaticFingerprint,
                    PreservedFingerprint,
                    RoutingFingerprint,
                    -1,
                    null)))
        .IsFalse();

    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOutcome(
                CreateSuccessOutcome()))
        .IsTrue();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOutcome(
                CreateSuccessOutcome() with
                {
                    TargetDigest = null,
                }))
        .IsFalse()
        .Because("success requires an applied target digest");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOutcome(
                CreateSuccessOutcome() with
                {
                    TargetWorkerRevision = null,
                }))
        .IsFalse()
        .Because(
            "success requires a reported target worker revision so the "
            + "store never persists a succeeded row without one");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOutcome(
                CreateSuccessOutcome() with
                {
                    CurrentWorkers = null,
                }))
        .IsFalse()
        .Because(
            "success requires a reported CurrentWorkers count so the "
            + "outcome cannot claim convergence without evidence");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOutcome(
                CreateSuccessOutcome() with
                {
                    StaleWorkers = null,
                }))
        .IsFalse()
        .Because(
            "success requires a reported StaleWorkers count so the "
            + "outcome cannot claim convergence without evidence");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOutcome(
                new ImageRolloutCommandOutcome(
                    Guid.NewGuid(),
                    "failed",
                    "vibes",
                    null,
                    null,
                    null,
                    "current",
                    null,
                    null,
                    null,
                    Now)))
        .IsFalse()
        .Because("failure categories are bounded");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOutcome(
                new ImageRolloutCommandOutcome(
                    Guid.NewGuid(),
                    "indeterminate",
                    "timeout",
                    "Recovered after restart.",
                    null,
                    null,
                    "degraded",
                    null,
                    null,
                    "The connector lost the execution result.",
                    Now)))
        .IsTrue();

    // Distinct closed outcome vocabulary: recipe-not-allowed,
    // registry-not-allowed, and unsupported-topology must be accepted
    // as failure categories on the wire so operators can distinguish
    // these from the general not-allowed / unsupported buckets. These
    // categories map 1:1 to the connector executor's closed rejections
    // and to the SqliteImageRolloutCommandStore rejection cascade.
    foreach (var distinctCategory in new[]
    {
        "recipe-not-allowed",
        "registry-not-allowed",
        "unsupported-topology",
    })
    {
      await Assert.That(
              SyncConnectorUnitOfWork.IsValidImageRolloutOutcome(
                  new ImageRolloutCommandOutcome(
                      Guid.NewGuid(),
                      "rejected",
                      distinctCategory,
                      null,
                      null,
                      null,
                      "degraded",
                      null,
                      null,
                      null,
                      Now)))
          .IsTrue()
          .Because(
              $"{distinctCategory} is a distinct closed wire outcome "
              + "category and must never be collapsed to not-allowed "
              + "or unsupported on the boundary");
    }

    // Privacy safeguard: outcome LastError is bounded to a small closed
    // literal size; free-form host text of length > 128 is rejected on the
    // wire. Non-null LastError up to 128 characters is accepted.
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOutcome(
                CreateSuccessOutcome() with
                {
                    LastError = new string('e', 128),
                }))
        .IsTrue()
        .Because("128 is the maximum bounded LastError length");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutOutcome(
                CreateSuccessOutcome() with
                {
                    LastError = new string('e', 129),
                }))
        .IsFalse()
        .Because("LastError over 128 chars is rejected as unbounded");

    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutProgress(
                new ImageRolloutCommandProgress(
                    Guid.NewGuid(),
                    "queued",
                    Now)))
        .IsFalse()
        .Because("only claimed and started phases exist");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutTargetDigest(
                TargetDigest))
        .IsTrue();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutTargetDigest(
                "sha256:UPPERCASEHEX000000000000000000000000000000000000000000000000000000"))
        .IsFalse()
        .Because("digests are lowercase hex");
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutTargetPlatform(
                "linux/amd64"))
        .IsTrue();
    await Assert.That(
            SyncConnectorUnitOfWork.IsValidImageRolloutTargetPlatform(
                "windows/amd64"))
        .IsFalse();
  }

  private static ImageRolloutOperatorCapability CreateCapability() =>
      new(
          [
              new ImageRolloutOperatorProfile(
                  "default",
                  "linux/amd64",
                  "ghcr.io/example/runner:main",
                  CurrentImageDigest,
                  CurrentLocalImageId,
                  CurrentWorkerRevision,
                  StaticFingerprint,
                  PreservedFingerprint,
                  RoutingFingerprint,
                  7,
                  DesiredStateHash,
                  ["copilot-cli"],
                  true,
                  true,
                  null,
                  false,
                  30,
                  600,
                  1800,
                  "current",
                  4,
                  0),
          ]);

  private static ImageRolloutCommandProgress CreateProgress() =>
      new(Guid.NewGuid(), "claimed", Now);

  private static ImageRolloutCommandOutcome CreateSuccessOutcome() =>
      new(
          Guid.NewGuid(),
          "succeeded",
          null,
          "Applied target digest.",
          TargetDigest,
          CurrentWorkerRevision,
          "current",
          4,
          0,
          null,
          Now);

  private static ConnectorSynchronizationInput CreateInput(
      int protocolVersion,
      ImageRolloutOperatorCapability? capability,
      ImageRolloutCommandProgress? progress,
      ImageRolloutCommandOutcome? outcome) =>
      new(
          protocolVersion,
          "2.0.0",
          Now,
          [],
          null,
          null,
          null,
          null,
          null,
          null,
          capability,
          progress,
          outcome);

  private SyncConnectorUnitOfWork CreateUnitOfWork(
      Mock<IImageRolloutCommandStore> rolloutStore)
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
            It.IsNotNull<IFleetStorageTransaction>(),
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            "2.0.0",
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<IReadOnlyList<ManagerObservedState>>(
                profiles => profiles.Count == 0),
            It.Is<IReadOnlySet<string>>(
                accepted => accepted.Count == 0),
            It.Is<ConnectorCredentialUpdate>(update =>
                update.Kind == ConnectorCredentialUpdateKind.None),
            It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    fleetStore
        .Setup(store => store.ApplyHostHardwareAsync(
            It.IsNotNull<IFleetStorageTransaction>(),
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<IReadOnlyList<ManagerObservedState>>(
                profiles => profiles.Count == 0),
            It.Is<IReadOnlyCollection<string>>(
                profileIds => profileIds.Count == 0),
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
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
    var recoveryStore = _mocks.Create<IRecoveryCommandStore>();
    recoveryStore
        .Setup(store => store.ApplyConnectorSyncAsync(
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<RecoveryOperatorCapability?>(capability => capability == null),
            It.Is<RecoveryCommandProgress?>(progress => progress == null),
            It.Is<RecoveryCommandOutcome?>(outcome => outcome == null),
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<DateTimeOffset>(redeliverBefore => redeliverBefore < Now),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((RecoverManagerCommand?)null);
    var historyStore = _mocks.Create<IFleetHistoryStore>();
    historyStore
        .Setup(store => store.AppendAsync(
            It.IsNotNull<IFleetStorageTransaction>(),
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<IReadOnlyList<ManagerObservedState>>(
                profiles => profiles.Count == 0),
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<HistoryAppendPolicy>(
                policy => policy.Retention.MaximumSamplesPerProfile > 0),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    historyStore
        .Setup(store => store.EnforceRetentionAsync(
            It.IsNotNull<IFleetStorageTransaction>(),
            It.Is<Guid>(nodeId => nodeId != Guid.Empty),
            It.Is<DateTimeOffset>(receivedAt => receivedAt == Now),
            It.Is<HistoryRetentionPolicy>(
                retention => retention.MaximumSamplesPerProfile > 0),
            It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    var connectorHealthStore = _mocks.Create<IConnectorHealthStore>();
    var transactionFactory = _mocks.Create<IFleetStorageTransactionFactory>();
    var transaction = _mocks.Create<IFleetStorageTransaction>();
    transaction
        .Setup(scope => scope.CommitAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    transaction
        .Setup(scope => scope.DisposeAsync())
        .Returns(ValueTask.CompletedTask);
    transactionFactory
        .Setup(factory => factory.BeginAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(transaction.Object);
    return new SyncConnectorUnitOfWork(
        fleetStore.Object,
        historyStore.Object,
        connectorHealthStore.Object,
        transactionFactory.Object,
        capacityStore.Object,
        recoveryStore.Object,
        rolloutStore.Object,
        new ConnectorCredentialService(),
        Options.Create(new FleetDashboardOptions()),
        new FixedTimeProvider(Now));
  }

  private sealed class FixedTimeProvider(DateTimeOffset _now) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => _now;
  }
}
