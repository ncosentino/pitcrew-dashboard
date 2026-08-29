using System.Security.Claims;

using Moq;

using PitCrew.Dashboard.Features.Images.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;
using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Images.Tests;

public sealed class RollOutProfileImageOrchestratorTests
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

  private const string TenantId = "tenant";
  private const string RecipeId = "copilot-cli";
  private const string IdempotencyKey = "orchestrator-test-key-01";
  private const string TargetDigest =
      "sha256:0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
  private const string RegistryReference =
      "ghcr.io/example/runner@sha256:0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
  private const string StaticFingerprint =
      "a1b2c3d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff0";
  private const string PreservedFingerprint =
      "b2c3d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001";
  private const string RoutingFingerprint =
      "c3d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff00112";
  private const string DesiredStateHash =
      "e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001a1b2c3";

  [Test]
  public async Task Queue_Succeeds_When_Ready_Registry_Candidate_And_Fleet_Accept()
  {
    var candidateId = Guid.NewGuid();
    var nodeId = Guid.NewGuid();
    var commandId = Guid.NewGuid();
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    candidateStore
        .Setup(store => store.GetCandidateOrNullAsync(
            TenantId,
            candidateId,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateReadyCandidate(candidateId));
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    SetupNoExistingReplay(unitOfWork);
    unitOfWork
        .Setup(uow => uow.QueueOrNullAsync(
            It.Is<ClaimsPrincipal>(principal =>
                principal.HasClaim(
                    PitCrewClaimTypes.GitHubUserId,
                    "42")),
            TenantId,
            nodeId,
            "default",
            It.Is<ImageRolloutCandidateAuthority>(candidate =>
                candidate.CandidateId == candidateId &&
                candidate.RecipeId == RecipeId &&
                candidate.TargetDigest == TargetDigest &&
                candidate.TargetPlatform == "linux/amd64"),
            It.Is<ImageRolloutCommandFences>(fences =>
                fences.ExpectedStaticFingerprint == StaticFingerprint &&
                fences.ExpectedPreservedConfigurationFingerprint
                    == PreservedFingerprint &&
                fences.ExpectedRoutingFingerprint == RoutingFingerprint &&
                fences.ExpectedDesiredGeneration == 7 &&
                fences.ExpectedDesiredStateHash == DesiredStateHash),
            It.Is<string>(key =>
                string.Equals(key, IdempotencyKey, StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ImageRolloutCommandQueueResult(
            ImageRolloutCommandQueueStatus.Queued,
            commandId));
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);

    var result = await orchestrator.QueueAsync(
        CreatePrincipal("42"),
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);

    await Assert.That(result.Status)
        .IsEqualTo(RollOutProfileImageStatus.Queued);
    await Assert.That(result.CommandId).IsEqualTo(commandId);
    await Assert.That(result.Code).IsNull();
    await Assert.That(result.Error).IsNull();
  }

  [Test]
  public async Task Missing_And_Unready_Candidates_Are_Not_Queued()
  {
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    SetupNoExistingReplay(unitOfWork);
    var missingCandidateId = Guid.NewGuid();
    var failedCandidateId = Guid.NewGuid();
    var ociCandidateId = Guid.NewGuid();
    candidateStore
        .Setup(store => store.GetCandidateOrNullAsync(
            TenantId,
            missingCandidateId,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((ImageCandidateDetails?)null);
    candidateStore
        .Setup(store => store.GetCandidateOrNullAsync(
            TenantId,
            failedCandidateId,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateFailedCandidate(failedCandidateId));
    candidateStore
        .Setup(store => store.GetCandidateOrNullAsync(
            TenantId,
            ociCandidateId,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateReadyCandidate(
            ociCandidateId,
            outputMode: ImageCandidateOutputMode.Oci,
            immutableReference: null));
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);
    var principal = CreatePrincipal("42");
    var nodeId = Guid.NewGuid();

    var missing = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, missingCandidateId),
        CancellationToken.None);
    var failed = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, failedCandidateId),
        CancellationToken.None);
    var ociOnly = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, ociCandidateId),
        CancellationToken.None);

    await Assert.That(missing.Status)
        .IsEqualTo(RollOutProfileImageStatus.CandidateNotFound);
    await Assert.That(missing.Code).IsEqualTo("image_candidate_not_found");
    await Assert.That(failed.Status)
        .IsEqualTo(RollOutProfileImageStatus.CandidateFailed);
    await Assert.That(failed.Code).IsEqualTo("image_candidate_not_ready");
    await Assert.That(ociOnly.Status)
        .IsEqualTo(RollOutProfileImageStatus.CandidateNotRegistryReady);
    await Assert.That(ociOnly.Code)
        .IsEqualTo("image_candidate_not_registry_ready");
    unitOfWork.Verify(
        uow => uow.QueueOrNullAsync(
            It.Is<ClaimsPrincipal>(_ => true),
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.Is<string>(_ => true),
            It.Is<ImageRolloutCandidateAuthority>(_ => true),
            It.Is<ImageRolloutCommandFences>(_ => true),
            It.Is<string>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Input_Validation_Rejects_Bad_Fingerprints_And_Non_Digest_Fences()
  {
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);
    var principal = CreatePrincipal("42");
    var candidateId = Guid.NewGuid();
    var nodeId = Guid.NewGuid();

    var missingProfileId = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ProfileId = "",
        },
        CancellationToken.None);
    var badFingerprint = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ExpectedStaticFingerprint = "not-a-hash",
        },
        CancellationToken.None);
    var badDigest = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ExpectedCurrentImageDigest = "sha256:notdigest",
        },
        CancellationToken.None);
    var negativeGeneration = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ExpectedDesiredGeneration = -1,
        },
        CancellationToken.None);
    var emptyNode = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          NodeId = Guid.Empty,
        },
        CancellationToken.None);

    await Assert.That(missingProfileId.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(badFingerprint.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(badDigest.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(negativeGeneration.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(emptyNode.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    candidateStore.Verify(
        store => store.GetCandidateOrNullAsync(
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Input_Validation_Rejects_Non_Contract_ProfileId()
  {
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);
    var principal = CreatePrincipal("42");
    var candidateId = Guid.NewGuid();
    var nodeId = Guid.NewGuid();

    var uppercase = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ProfileId = "Default",
        },
        CancellationToken.None);
    var digitPrefix = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ProfileId = "1default",
        },
        CancellationToken.None);
    var tooLong = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ProfileId = new string('x', 33),
        },
        CancellationToken.None);

    await Assert.That(uppercase.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(digitPrefix.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(tooLong.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    candidateStore.Verify(
        store => store.GetCandidateOrNullAsync(
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Input_Validation_Rejects_Malformed_Expected_Current_Image_Reference()
  {
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);
    var principal = CreatePrincipal("42");
    var candidateId = Guid.NewGuid();
    var nodeId = Guid.NewGuid();

    var whitespaceInside = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ExpectedCurrentImageReference = "ghcr.io/example/runner :main",
        },
        CancellationToken.None);
    var controlChar = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ExpectedCurrentImageReference = "ghcr.io/example/runner\x01:main",
        },
        CancellationToken.None);
    var quote = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ExpectedCurrentImageReference = "ghcr.io/\"example\"/runner:main",
        },
        CancellationToken.None);
    var backslash = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ExpectedCurrentImageReference = "ghcr.io\\example/runner:main",
        },
        CancellationToken.None);
    var oversized = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ExpectedCurrentImageReference = new string('x', 513),
        },
        CancellationToken.None);
    var empty = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId) with
        {
          ExpectedCurrentImageReference = string.Empty,
        },
        CancellationToken.None);

    await Assert.That(whitespaceInside.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(controlChar.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(quote.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(backslash.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(oversized.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(empty.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid)
        .Because("empty string is length 0 which is not the same as null");
    candidateStore.Verify(
        store => store.GetCandidateOrNullAsync(
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Fleet_Queue_Statuses_Map_To_Distinct_Orchestrator_Statuses()
  {
    var candidateId = Guid.NewGuid();
    var nodeId = Guid.NewGuid();
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    candidateStore
        .Setup(store => store.GetCandidateOrNullAsync(
            TenantId,
            candidateId,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateReadyCandidate(candidateId));
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    SetupNoExistingReplay(unitOfWork);
    var results = new Queue<ImageRolloutCommandQueueResult?>(
        [
            new ImageRolloutCommandQueueResult(
                ImageRolloutCommandQueueStatus.NodeNotFound,
                null),
            new ImageRolloutCommandQueueResult(
                ImageRolloutCommandQueueStatus.Unsupported,
                null),
            new ImageRolloutCommandQueueResult(
                ImageRolloutCommandQueueStatus.UnsupportedTopology,
                null),
            new ImageRolloutCommandQueueResult(
                ImageRolloutCommandQueueStatus.NotAllowed,
                null),
            new ImageRolloutCommandQueueResult(
                ImageRolloutCommandQueueStatus.RecipeNotAllowed,
                null),
            new ImageRolloutCommandQueueResult(
                ImageRolloutCommandQueueStatus.RegistryNotAllowed,
                null),
            new ImageRolloutCommandQueueResult(
                ImageRolloutCommandQueueStatus.ArchitectureMismatch,
                null),
            new ImageRolloutCommandQueueResult(
                ImageRolloutCommandQueueStatus.StaleFence,
                null),
            new ImageRolloutCommandQueueResult(
                ImageRolloutCommandQueueStatus.Conflict,
                null),
            new ImageRolloutCommandQueueResult(
                ImageRolloutCommandQueueStatus.RateLimited,
                null),
            null,
        ]);
    unitOfWork
        .Setup(uow => uow.QueueOrNullAsync(
            It.Is<ClaimsPrincipal>(_ => true),
            TenantId,
            nodeId,
            "default",
            It.Is<ImageRolloutCandidateAuthority>(_ => true),
            It.Is<ImageRolloutCommandFences>(_ => true),
            It.Is<string>(_ => true),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => results.Dequeue());
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);
    var principal = CreatePrincipal("42");

    var nodeNotFound = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);
    var unsupported = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);
    var unsupportedTopology = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);
    var notAllowed = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);
    var recipeNotAllowed = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);
    var registryNotAllowed = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);
    var architectureMismatch = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);
    var staleFence = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);
    var conflict = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);
    var rateLimited = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);
    var unauthenticated = await orchestrator.QueueAsync(
        principal,
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);

    await Assert.That(nodeNotFound.Status)
        .IsEqualTo(RollOutProfileImageStatus.CandidateNotFound);
    await Assert.That(unsupported.Status)
        .IsEqualTo(RollOutProfileImageStatus.Unsupported);
    await Assert.That(unsupportedTopology.Status)
        .IsEqualTo(RollOutProfileImageStatus.UnsupportedTopology);
    await Assert.That(notAllowed.Status)
        .IsEqualTo(RollOutProfileImageStatus.NotAllowed);
    await Assert.That(recipeNotAllowed.Status)
        .IsEqualTo(RollOutProfileImageStatus.RecipeNotAllowed);
    await Assert.That(registryNotAllowed.Status)
        .IsEqualTo(RollOutProfileImageStatus.RegistryNotAllowed);
    await Assert.That(architectureMismatch.Status)
        .IsEqualTo(RollOutProfileImageStatus.ArchitectureMismatch);
    await Assert.That(staleFence.Status)
        .IsEqualTo(RollOutProfileImageStatus.StaleFence);
    await Assert.That(conflict.Status)
        .IsEqualTo(RollOutProfileImageStatus.Conflict);
    await Assert.That(rateLimited.Status)
        .IsEqualTo(RollOutProfileImageStatus.RateLimited);
    await Assert.That(unauthenticated.Status)
        .IsEqualTo(RollOutProfileImageStatus.Unauthorized);
    await Assert.That(nodeNotFound.CommandId).IsNull();
    await Assert.That(unsupportedTopology.Code)
        .IsEqualTo("image_rollout_unsupported_topology");
    await Assert.That(recipeNotAllowed.Code)
        .IsEqualTo("image_rollout_recipe_not_allowed");
    await Assert.That(registryNotAllowed.Code)
        .IsEqualTo("image_rollout_registry_not_allowed");
    await Assert.That(rateLimited.Code).IsEqualTo("image_rollout_cooldown");
    await Assert.That(unauthenticated.Code)
        .IsEqualTo("unauthorized_image_rollout");
  }

  [Test]
  public async Task Idempotent_Replay_Returns_Same_Command_Before_Candidate_Load()
  {
    // Finding: replay resolution must precede candidate lookup so a
    // durable command id still resolves after candidate retention
    // removes the immutable candidate row. This test proves the
    // orchestrator returns the durable command from the pre-candidate
    // probe alone and never touches the candidate store.
    var candidateId = Guid.NewGuid();
    var nodeId = Guid.NewGuid();
    var existingCommandId = Guid.NewGuid();
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    unitOfWork
        .Setup(uow => uow.LookupReplayOrNullAsync(
            It.Is<ClaimsPrincipal>(principal =>
                principal.HasClaim(
                    PitCrewClaimTypes.GitHubUserId,
                    "42")),
            TenantId,
            nodeId,
            "default",
            candidateId,
            It.Is<ImageRolloutCommandFences>(fences =>
                fences.ExpectedStaticFingerprint == StaticFingerprint &&
                fences.ExpectedPreservedConfigurationFingerprint
                    == PreservedFingerprint &&
                fences.ExpectedRoutingFingerprint == RoutingFingerprint &&
                fences.ExpectedDesiredGeneration == 7 &&
                fences.ExpectedDesiredStateHash == DesiredStateHash),
            It.Is<string>(key =>
                string.Equals(key, IdempotencyKey, StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ImageRolloutIdempotencyLookup(
            ImageRolloutIdempotencyLookupOutcome.IdempotentReplay,
            existingCommandId));
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);

    var replay = await orchestrator.QueueAsync(
        CreatePrincipal("42"),
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);

    await Assert.That(replay.Status)
        .IsEqualTo(RollOutProfileImageStatus.IdempotentReplay);
    await Assert.That(replay.CommandId).IsEqualTo(existingCommandId);
    await Assert.That(replay.Code).IsNull();
    await Assert.That(replay.Error).IsNull();
    // The candidate store must never be touched: this is exactly the
    // retention-durability property the pre-candidate probe delivers.
    candidateStore.Verify(
        store => store.GetCandidateOrNullAsync(
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
    unitOfWork.Verify(
        uow => uow.QueueOrNullAsync(
            It.Is<ClaimsPrincipal>(_ => true),
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.Is<string>(_ => true),
            It.Is<ImageRolloutCandidateAuthority>(_ => true),
            It.Is<ImageRolloutCommandFences>(_ => true),
            It.Is<string>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Replay_Resolves_Even_When_Candidate_Retention_Removed_Candidate()
  {
    // Explicit durability regression: an exact retry of the same key
    // must still return the durable command id even when the immutable
    // candidate row has been retention-pruned. The candidate store is
    // configured to return null; if the orchestrator reached candidate
    // lookup at all it would surface CandidateNotFound and defeat
    // at-most-once durability.
    var candidateId = Guid.NewGuid();
    var nodeId = Guid.NewGuid();
    var durableCommandId = Guid.NewGuid();
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    candidateStore
        .Setup(store => store.GetCandidateOrNullAsync(
            TenantId,
            candidateId,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((ImageCandidateDetails?)null);
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    unitOfWork
        .Setup(uow => uow.LookupReplayOrNullAsync(
            It.Is<ClaimsPrincipal>(_ => true),
            TenantId,
            nodeId,
            "default",
            candidateId,
            It.Is<ImageRolloutCommandFences>(_ => true),
            It.Is<string>(_ => true),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ImageRolloutIdempotencyLookup(
            ImageRolloutIdempotencyLookupOutcome.IdempotentReplay,
            durableCommandId));
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);

    var replay = await orchestrator.QueueAsync(
        CreatePrincipal("42"),
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);

    await Assert.That(replay.Status)
        .IsEqualTo(RollOutProfileImageStatus.IdempotentReplay);
    await Assert.That(replay.CommandId).IsEqualTo(durableCommandId);
    candidateStore.Verify(
        store => store.GetCandidateOrNullAsync(
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Idempotency_Key_Reuse_Conflict_Returns_Stable_Code_Before_Candidate_Load()
  {
    // Finding: a same-key/different-authority conflict must be resolved
    // pre-candidate so a mismatched request never causes a candidate
    // read (and never leaks whether a candidate exists) and does not
    // reach QueueOrNullAsync. Same key + different fences must still
    // conflict at the durable-replay boundary.
    var candidateId = Guid.NewGuid();
    var nodeId = Guid.NewGuid();
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    unitOfWork
        .Setup(uow => uow.LookupReplayOrNullAsync(
            It.Is<ClaimsPrincipal>(_ => true),
            TenantId,
            nodeId,
            "default",
            candidateId,
            It.Is<ImageRolloutCommandFences>(_ => true),
            It.Is<string>(_ => true),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ImageRolloutIdempotencyLookup(
            ImageRolloutIdempotencyLookupOutcome.IdempotencyKeyReuseConflict,
            null));
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);

    var conflict = await orchestrator.QueueAsync(
        CreatePrincipal("42"),
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);

    await Assert.That(conflict.Status)
        .IsEqualTo(RollOutProfileImageStatus.IdempotencyKeyReuseConflict);
    await Assert.That(conflict.CommandId).IsNull();
    await Assert.That(conflict.Code)
        .IsEqualTo("image_rollout_idempotency_key_conflict");
    candidateStore.Verify(
        store => store.GetCandidateOrNullAsync(
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
    unitOfWork.Verify(
        uow => uow.QueueOrNullAsync(
            It.Is<ClaimsPrincipal>(_ => true),
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.Is<string>(_ => true),
            It.Is<ImageRolloutCandidateAuthority>(_ => true),
            It.Is<ImageRolloutCommandFences>(_ => true),
            It.Is<string>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Unauthorized_Principal_Returns_Unauthorized_Before_Candidate_Load()
  {
    // The pre-candidate probe also fails closed for unauthorized
    // principals: LookupReplayOrNullAsync returns null (unauthorized),
    // so the orchestrator must surface Unauthorized without touching
    // the candidate store and without invoking QueueOrNullAsync.
    var candidateId = Guid.NewGuid();
    var nodeId = Guid.NewGuid();
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    unitOfWork
        .Setup(uow => uow.LookupReplayOrNullAsync(
            It.Is<ClaimsPrincipal>(_ => true),
            TenantId,
            nodeId,
            "default",
            candidateId,
            It.Is<ImageRolloutCommandFences>(_ => true),
            It.Is<string>(_ => true),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((ImageRolloutIdempotencyLookup?)null);
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);

    var result = await orchestrator.QueueAsync(
        CreatePrincipal("42"),
        TenantId,
        CreateInput(nodeId, candidateId),
        CancellationToken.None);

    await Assert.That(result.Status)
        .IsEqualTo(RollOutProfileImageStatus.Unauthorized);
    await Assert.That(result.CommandId).IsNull();
    await Assert.That(result.Code).IsEqualTo("unauthorized_image_rollout");
    candidateStore.Verify(
        store => store.GetCandidateOrNullAsync(
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
    unitOfWork.Verify(
        uow => uow.QueueOrNullAsync(
            It.Is<ClaimsPrincipal>(_ => true),
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.Is<string>(_ => true),
            It.Is<ImageRolloutCandidateAuthority>(_ => true),
            It.Is<ImageRolloutCommandFences>(_ => true),
            It.Is<string>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [Test]
  public async Task Idempotency_Key_Validation_Rejects_Missing_And_Malformed_Keys()
  {
    var candidateId = Guid.NewGuid();
    var nodeId = Guid.NewGuid();
    var candidateStore = _mocks.Create<IImageCandidateStore>();
    var unitOfWork = _mocks.Create<IRollOutProfileImageUnitOfWork>();
    var orchestrator = new RollOutProfileImageOrchestrator(
        candidateStore.Object,
        unitOfWork.Object);
    var principal = CreatePrincipal("42");
    var baseInput = CreateInput(nodeId, candidateId);

    var missing = await orchestrator.QueueAsync(
        principal,
        TenantId,
        baseInput with { IdempotencyKey = string.Empty },
        CancellationToken.None);
    var blank = await orchestrator.QueueAsync(
        principal,
        TenantId,
        baseInput with { IdempotencyKey = "   " },
        CancellationToken.None);
    var tooShort = await orchestrator.QueueAsync(
        principal,
        TenantId,
        baseInput with { IdempotencyKey = "abc" },
        CancellationToken.None);
    var tooLong = await orchestrator.QueueAsync(
        principal,
        TenantId,
        baseInput with { IdempotencyKey = new string('a', 201) },
        CancellationToken.None);
    var badChars = await orchestrator.QueueAsync(
        principal,
        TenantId,
        baseInput with { IdempotencyKey = "invalid space" },
        CancellationToken.None);
    var accepted = new[]
    {
        new string('a', 8),
        new string('a', 200),
        "abc.def-ghi_jkl:mno",
    };

    await Assert.That(missing.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(blank.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(tooShort.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(tooLong.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    await Assert.That(badChars.Status)
        .IsEqualTo(RollOutProfileImageStatus.Invalid);
    candidateStore.Verify(
        store => store.GetCandidateOrNullAsync(
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
    unitOfWork.Verify(
        uow => uow.QueueOrNullAsync(
            It.Is<ClaimsPrincipal>(_ => true),
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.Is<string>(_ => true),
            It.Is<ImageRolloutCandidateAuthority>(_ => true),
            It.Is<ImageRolloutCommandFences>(_ => true),
            It.Is<string>(_ => true),
            It.IsAny<CancellationToken>()),
        Times.Never);
    foreach (var key in accepted)
    {
      await Assert.That(RollOutProfileImageOrchestrator.IsValidIdempotencyKey(key))
          .IsTrue()
          .Because($"key '{key}' is within the accepted shape");
    }
  }

  private static ClaimsPrincipal CreatePrincipal(string githubUserId) =>
      new(new ClaimsIdentity(
          [
              new Claim(PitCrewClaimTypes.GitHubUserId, githubUserId),
              new Claim(PitCrewClaimTypes.GitHubLogin, "operator"),
          ],
          "test"));

  // Strict-mock helper: the orchestrator now probes for durable
  // idempotency replay before any candidate work, so every test whose
  // intent is to exercise the post-candidate flow must first tell the
  // UoW that no prior command exists for the caller's key. Tests that
  // deliberately assert replay/conflict short-circuit behaviour set up
  // LookupReplayOrNullAsync directly and must not use this helper.
  private static void SetupNoExistingReplay(
      Mock<IRollOutProfileImageUnitOfWork> unitOfWork)
  {
    unitOfWork
        .Setup(uow => uow.LookupReplayOrNullAsync(
            It.Is<ClaimsPrincipal>(_ => true),
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.Is<string>(_ => true),
            It.Is<Guid>(_ => true),
            It.Is<ImageRolloutCommandFences>(_ => true),
            It.Is<string>(_ => true),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ImageRolloutIdempotencyLookup(
            ImageRolloutIdempotencyLookupOutcome.NoExistingCommand,
            null));
  }

  private static RollOutProfileImageInput CreateInput(
      Guid nodeId,
      Guid candidateId) =>
      new(
          nodeId,
          "default",
          candidateId,
          "ghcr.io/example/runner:main",
          "sha256:1111111111111111111111111111111111111111111111111111111111111111",
          "sha256:2222222222222222222222222222222222222222222222222222222222222222",
          "d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001a1b2",
          StaticFingerprint,
          PreservedFingerprint,
          RoutingFingerprint,
          7,
          DesiredStateHash,
          IdempotencyKey);

  private static ImageCandidateDetails CreateReadyCandidate(
      Guid candidateId,
      ImageCandidateOutputMode outputMode = ImageCandidateOutputMode.Registry,
      string? immutableReference = RegistryReference) =>
      new(
          new ReadyImageCandidate(
              candidateId,
              TenantId,
              Guid.NewGuid(),
              RecipeId,
              "ncosentino/pitcrew-dashboard",
              new string('a', 40),
              1_001,
              1_002,
              "candidate.tar.gz",
              new string('b', 64),
              new string('c', 64),
              "{\"schemaVersion\":1}",
              "ghcr.io/example/runner:main",
              ImageCandidatePlatform.LinuxAmd64,
              outputMode,
              Now.AddMinutes(-10),
              Now.AddMinutes(-5),
              TargetDigest,
              immutableReference),
          Guid.NewGuid(),
          1,
          null,
          null,
          []);

  private static ImageCandidateDetails CreateFailedCandidate(
      Guid candidateId) =>
      new(
          new FailedImageCandidate(
              candidateId,
              TenantId,
              Guid.NewGuid(),
              RecipeId,
              "ncosentino/pitcrew-dashboard",
              new string('a', 40),
              1_001,
              1_002,
              "candidate.tar.gz",
              new string('b', 64),
              new string('c', 64),
              "{\"schemaVersion\":1}",
              "ghcr.io/example/runner:main",
              ImageCandidatePlatform.LinuxAmd64,
              ImageCandidateOutputMode.Registry,
              Now.AddMinutes(-10),
              Now.AddMinutes(-5),
              null,
              null,
              "workflow-failed",
              "The workflow failed."),
          Guid.NewGuid(),
          1,
          null,
          null,
          []);
}
