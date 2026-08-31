using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteImageRolloutCommandStoreTests
{
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
  private const string TargetDigest =
      "sha256:0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
  private const string CurrentWorkerRevision =
      "d4e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001a1b2";
  private const string DesiredStateHash =
      "e5f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001a1b2c3";
  private const string TargetWorkerRevision =
      "f60718293a4b5c6d7e8f902112233445566778899aabbccddeeff001a1b2c3d4";
  // A worker revision that differs from both CurrentWorkerRevision and
  // TargetWorkerRevision. Represents post-success worker drift caused by
  // a non-image profile change (capacity, routing, or a manual redeploy)
  // that leaves the image digest untouched but shifts the applied
  // revision. Distinct 64-lowercase-hex value.
  private const string DriftedWorkerRevision =
      "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
  private const string SecondTargetDigest =
      "sha256:2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a";
  private const string StaticFingerprintAfterSuccess =
      "9988776655443322110099aabbccddeeff00112233445566778899aabbccddee";
  private const string RegistryRepository = "ghcr.io/example/runner";
  private const string RecipeId = "copilot-cli";

  [Test]
  public async Task Rollout_Command_Is_Delivered_Once_And_Progresses_To_Success(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);

      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);
      await Assert.That(queued.CommandId).IsNotNull();

      var firstOffer = await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);
      await Assert.That(firstOffer).IsNotNull();
      await Assert.That(firstOffer!.CommandId)
          .IsEqualTo(queued.CommandId!.Value);
      await Assert.That(firstOffer.TargetDigest).IsEqualTo(TargetDigest);
      await Assert.That(firstOffer.TargetPlatform).IsEqualTo("linux/amd64");

      var immediateReoffer = await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(2),
          cancellationToken);
      await Assert.That(immediateReoffer)
          .IsNull()
          .Because("delivery is only repeated after the redelivery window");

      var redelivered = await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          null,
          Now.AddSeconds(200),
          Now.AddSeconds(80),
          cancellationToken);
      await Assert.That(redelivered).IsNotNull();

      var claimed = await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          new ImageRolloutCommandProgress(
              queued.CommandId!.Value,
              "claimed",
              Now.AddSeconds(210)),
          null,
          Now.AddSeconds(211),
          Now.AddSeconds(150),
          cancellationToken);
      await Assert.That(claimed)
          .IsNull()
          .Because("a claimed command is never offered again");

      var started = await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          new ImageRolloutCommandProgress(
              queued.CommandId!.Value,
              "started",
              Now.AddSeconds(215)),
          null,
          Now.AddSeconds(216),
          Now.AddSeconds(150),
          cancellationToken);
      await Assert.That(started).IsNull();

      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              queued.CommandId!.Value,
              "succeeded",
              null,
              "Applied target digest.",
              TargetDigest,
              TargetWorkerRevision,
              "current",
              4,
              0,
              null,
              Now.AddSeconds(220)),
          Now.AddSeconds(221),
          Now.AddSeconds(150),
          cancellationToken);

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(controls).HasSingleItem();
      var command = controls[0].Profiles[0].LatestCommand;
      await Assert.That(command).IsNotNull();
      await Assert.That(command!.Status).IsEqualTo("succeeded");
      await Assert.That(command.RequestedByGitHubUserId).IsEqualTo("1");
      await Assert.That(command.TargetDigest).IsEqualTo(TargetDigest);
      await Assert.That(command.TargetWorkerRevision)
          .IsEqualTo(TargetWorkerRevision);
      await Assert.That(command.ManagerConvergenceStatus).IsEqualTo("current");
      await Assert.That(command.CurrentWorkers).IsEqualTo(4);
      await Assert.That(command.StaleWorkers).IsEqualTo(0);
      await Assert.That(command.StartedAt).IsNotNull();
      await Assert.That(command.FailureCategory).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Terminal_Outcome_And_Audit_Actor_Are_Immutable(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);
      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              queued.CommandId!.Value,
              "failed",
              "process-failure",
              "Rollout process exited with a failure.",
              TargetDigest,
              null,
              "degraded",
              null,
              null,
              "exit 1",
              Now.AddSeconds(30)),
          Now.AddSeconds(31),
          Now.AddSeconds(-60),
          cancellationToken);

      var laterOutcome = await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              queued.CommandId!.Value,
              "succeeded",
              null,
              "Late duplicate outcome.",
              TargetDigest,
              TargetWorkerRevision,
              "current",
              4,
              0,
              null,
              Now.AddSeconds(60)),
          Now.AddSeconds(61),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(laterOutcome).IsNull();

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(controls[0].Profiles[0].LatestCommand!.Status)
          .IsEqualTo("failed");

      await Assert.That(
              async () => await ExecuteAsync(
                  connectionFactory,
                  """
                  UPDATE image_rollout_commands
                  SET status = 'succeeded'
                  WHERE command_id = $commandId;
                  """,
                  queued.CommandId!.Value,
                  cancellationToken))
          .Throws<SqliteException>()
          .Because("terminal outcomes are immutable");

      await Assert.That(
              async () => await ExecuteAsync(
                  connectionFactory,
                  """
                  UPDATE image_rollout_commands
                  SET requested_by_github_user_id = '2'
                  WHERE command_id = $commandId;
                  """,
                  queued.CommandId!.Value,
                  cancellationToken))
          .Throws<SqliteException>()
          .Because("audit actors are immutable");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Capacity_Recovery_And_Rollout_Operations_Are_Mutually_Exclusive(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var rolloutStore = new SqliteImageRolloutCommandStore(connectionFactory);
      var capacityStore = new SqliteCapacityCommandStore(connectionFactory);
      await SynchronizeAsync(rolloutStore, nodeId, Now, cancellationToken);
      await capacityStore.ApplyConnectorSyncAsync(
          nodeId,
          new CapacityOperatorCapability(
              [
                  new CapacityOperatorProfile(
                      "default",
                      4,
                      10,
                      50),
              ]),
          null,
          Now,
          Now.AddSeconds(-60),
          cancellationToken);

      var queuedRollout = await QueueAsync(rolloutStore, nodeId, Now, cancellationToken);
      await Assert.That(queuedRollout.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      var blockedCapacity = await capacityStore.QueueAsync(
          "tenant",
          nodeId,
          "default",
          20,
          "1",
          Now.AddSeconds(1),
          Now.AddMinutes(10),
          cancellationToken);
      await Assert.That(blockedCapacity.Status)
          .IsEqualTo(CapacityCommandQueueStatus.Conflict);

      var blockedRollout = await QueueAsync(
          rolloutStore,
          nodeId,
          Now.AddSeconds(120),
          cancellationToken);
      await Assert.That(blockedRollout.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Conflict);

      await rolloutStore.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              queuedRollout.CommandId!.Value,
              "rejected",
              "not-allowed",
              "Local policy rejected the command.",
              null,
              null,
              "degraded",
              null,
              null,
              null,
              Now.AddSeconds(150)),
          Now.AddSeconds(151),
          Now.AddSeconds(60),
          cancellationToken);

      var allowedCapacity = await capacityStore.QueueAsync(
          "tenant",
          nodeId,
          "default",
          20,
          "1",
          Now.AddSeconds(160),
          Now.AddMinutes(20),
          cancellationToken);
      await Assert.That(allowedCapacity.Status)
          .IsEqualTo(CapacityCommandQueueStatus.Queued)
          .Because("the profile operation slot is released on terminal outcomes");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Unsupported_Disallowed_Stale_And_Repeated_Requests_Are_Rejected(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);

      var unsupported = await QueueAsync(
          store,
          nodeId,
          Now,
          cancellationToken);
      await Assert.That(unsupported.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Unsupported);

      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability() with
          {
            Profiles =
            [
                CreateCapability().Profiles[0] with
                {
                  RolloutAllowed = false,
                },
            ],
          },
          null,
          null,
          Now,
          Now.AddSeconds(-60),
          cancellationToken);
      var notAllowed = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);
      await Assert.That(notAllowed.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.NotAllowed);

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(2),
          cancellationToken);
      var staleFingerprint = new string('a', 64);
      var staleFence = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          CreateCandidateAuthority(),
          new ImageRolloutCommandFences(
              null,
              null,
              null,
              null,
              staleFingerprint,
              PreservedFingerprint,
              RoutingFingerprint,
              7,
              null),
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          Now.AddSeconds(3),
          Now.AddMinutes(10),
          Now.AddSeconds(-117),
          Now.AddSeconds(-57),
          cancellationToken);
      await Assert.That(staleFence.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.StaleFence);

      var staleCapability = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(1000),
          cancellationToken);
      await Assert.That(staleCapability.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.StaleFence)
          .Because("queueing requires a fresh connector projection");

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(1001),
          cancellationToken);
      var wrongArchitecture = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          new ImageRolloutCandidateAuthority(
              Guid.NewGuid(),
              RecipeId,
              TargetDigest,
              "linux/arm64"),
          CreateFences(),
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          Now.AddSeconds(1002),
          Now.AddSeconds(1002).AddMinutes(10),
          Now.AddSeconds(1002).AddSeconds(-120),
          Now.AddSeconds(1002).AddSeconds(-60),
          cancellationToken);
      await Assert.That(wrongArchitecture.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.ArchitectureMismatch);

      var unknownRecipe = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          new ImageRolloutCandidateAuthority(
              Guid.NewGuid(),
              "not-allowed-recipe",
              TargetDigest,
              "linux/amd64"),
          CreateFences(),
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          Now.AddSeconds(1003),
          Now.AddSeconds(1003).AddMinutes(10),
          Now.AddSeconds(1003).AddSeconds(-120),
          Now.AddSeconds(1003).AddSeconds(-60),
          cancellationToken);
      await Assert.That(unknownRecipe.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.RecipeNotAllowed);

      var queued = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(1004),
          cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              queued.CommandId!.Value,
              "rejected",
              "stale-fence",
              "Fences changed locally.",
              null,
              null,
              "degraded",
              null,
              null,
              null,
              Now.AddSeconds(1005)),
          Now.AddSeconds(1006),
          Now.AddSeconds(-60),
          cancellationToken);
      var rateLimited = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(1007),
          cancellationToken);
      await Assert.That(rateLimited.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.RateLimited);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  // Confirms the LocalFailureCategory ⇒ Offer/Queue reject_category
  // normalization in SqliteImageRolloutCommandStore.OfferAsync stays
  // distinct: stale-observed-state maps to stale-fence (not the generic
  // NotAllowed), unsupported-schema/unsupported-manager map to unsupported,
  // and policy-disabled maps to not-allowed. This preserves visibility of
  // the underlying reason for later readers/campaigns without leaking the
  // closed set on the wire.
  [Test]
  [Arguments("stale-observed-state", "stale-fence")]
  [Arguments("unsupported-schema", "unsupported")]
  [Arguments("unsupported-manager", "unsupported")]
  [Arguments("unsupported-topology", "unsupported-topology")]
  [Arguments("unsupported-architecture", "unsupported-architecture")]
  [Arguments("registry-not-allowed", "registry-not-allowed")]
  [Arguments("policy-disabled", "not-allowed")]
  public async Task LocalFailureCategory_Maps_To_Distinct_Sqlite_Rejection(
      string capabilityCategory,
      string expectedFailureCategory,
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      // Baseline capability so QueueAsync succeeds and produces a durable
      // queued command that OfferAsync can then reject on the next sync
      // when the capability changes to one that carries the specific
      // LocalFailureCategory under test.
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      // Advertise the specific capability category the LocalFailureCategory
      // branch is supposed to normalize.
      var localSchemaSupported = capabilityCategory is
          not ("unsupported-schema" or "unsupported-manager");
      await store.ApplyConnectorSyncAsync(
          nodeId,
          new ImageRolloutOperatorCapability(
              [
                  CreateCapability().Profiles[0] with
                  {
                    RolloutAllowed = false,
                    LocalSchemaSupported = localSchemaSupported,
                    LocalFailureCategory = capabilityCategory,
                    ObservedStateAgeSeconds =
                        capabilityCategory == "stale-observed-state"
                            ? 86_400
                            : 30,
                  },
              ]),
          null,
          null,
          Now.AddSeconds(1),
          Now.AddSeconds(-60),
          cancellationToken);
      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(controls[0].Profiles[0].LatestCommand!.FailureCategory)
          .IsEqualTo(expectedFailureCategory)
          .Because(
              $"{capabilityCategory} must normalize to "
              + $"{expectedFailureCategory} in the SQLite rejection cascade "
              + "so operators see the exact reason, not generic not-allowed");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  // Recipe absence must produce the distinct RecipeNotAllowed queue
  // status so operators can tell a recipe-scoped rejection apart from a
  // profile-scoped policy rejection (NotAllowed).
  [Test]
  public async Task Queue_Rejects_Unallowlisted_Recipe_As_RecipeNotAllowed(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);

      var result = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          new ImageRolloutCandidateAuthority(
              Guid.NewGuid(),
              "not-on-allowlist",
              TargetDigest,
              "linux/amd64"),
          CreateFences(),
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          Now.AddSeconds(1),
          Now.AddSeconds(1).AddMinutes(10),
          Now.AddSeconds(1).AddSeconds(-120),
          Now.AddSeconds(1).AddSeconds(-60),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.RecipeNotAllowed)
          .Because(
              "candidate recipe absent from AllowedRecipeIds must land "
              + "in the recipe-not-allowed bucket, not the general "
              + "not-allowed bucket");
      await Assert.That(result.CommandId).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  // A capability that carries the distinct unsupported-topology
  // LocalFailureCategory must produce the distinct UnsupportedTopology
  // queue status, never a generic NotAllowed or Unsupported.
  [Test]
  public async Task Queue_Rejects_Unsupported_Topology_As_UnsupportedTopology(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);

      await store.ApplyConnectorSyncAsync(
          nodeId,
          new ImageRolloutOperatorCapability(
              [
                  CreateCapability().Profiles[0] with
                  {
                    RolloutAllowed = false,
                    LocalSchemaSupported = true,
                    LocalFailureCategory = "unsupported-topology",
                  },
              ]),
          null,
          null,
          Now,
          Now.AddSeconds(-60),
          cancellationToken);

      var result = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.UnsupportedTopology)
          .Because(
              "unsupported-topology is a distinct closed category and "
              + "must not collapse to Unsupported or NotAllowed");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Queue_Rejects_Invalid_Registry_Policy_As_RegistryNotAllowed(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          new ImageRolloutOperatorCapability(
              [
                  CreateCapability().Profiles[0] with
                  {
                    RolloutAllowed = false,
                    LocalFailureCategory = "registry-not-allowed",
                  },
              ]),
          null,
          null,
          Now,
          Now.AddSeconds(-60),
          cancellationToken);

      var result = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.RegistryNotAllowed);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Expiry_Resolves_Queued_And_Started_Commands_Differently(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddMinutes(30),
          cancellationToken);
      var expiredControls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(
              expiredControls[0].Profiles[0].LatestCommand!.Status)
          .IsEqualTo("expired");
      await Assert.That(
              expiredControls[0].Profiles[0].LatestCommand!.FailureCategory)
          .IsEqualTo("expired");
      await Assert.That(expiredControls[0].Profiles[0].LatestCommand!.CommandId)
          .IsEqualTo(queued.CommandId!.Value);

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddMinutes(31),
          cancellationToken);
      var second = await QueueAsync(
          store,
          nodeId,
          Now.AddMinutes(31),
          cancellationToken);
      await Assert.That(second.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          new ImageRolloutCommandProgress(
              second.CommandId!.Value,
              "started",
              Now.AddMinutes(32)),
          null,
          Now.AddMinutes(32),
          Now.AddMinutes(30),
          cancellationToken);

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddMinutes(60),
          cancellationToken);
      var indeterminateControls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(
              indeterminateControls[0].Profiles[0].LatestCommand!.Status)
          .IsEqualTo("indeterminate")
          .Because("a started command never re-executes after expiry");
      await Assert.That(
              indeterminateControls[0].Profiles[0].LatestCommand!.FailureCategory)
          .IsEqualTo("timeout");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Controls_Project_Newest_First_History_And_Are_Tenant_Isolated(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var first = await QueueAsync(store, nodeId, Now, cancellationToken);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              first.CommandId!.Value,
              "rejected",
              "not-allowed",
              "Local policy rejected the command.",
              null,
              null,
              "degraded",
              null,
              null,
              null,
              Now.AddSeconds(30)),
          Now.AddSeconds(31),
          Now.AddSeconds(-60),
          cancellationToken);

      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(300),
          cancellationToken);
      var second = await QueueAsync(
          store,
          nodeId,
          Now.AddSeconds(300),
          cancellationToken);
      await Assert.That(second.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      var profile = controls[0].Profiles[0];
      await Assert.That(profile.ObservedStateMaximumAgeSeconds).IsEqualTo(120);
      await Assert.That(profile.RecentCommands.Count).IsEqualTo(2);
      await Assert.That(profile.RecentCommands[0].CommandId)
          .IsEqualTo(second.CommandId!.Value);
      await Assert.That(profile.RecentCommands[1].CommandId)
          .IsEqualTo(first.CommandId!.Value);
      await Assert.That(profile.RecentCommands[1].Status)
          .IsEqualTo("rejected")
          .Because("terminal outcomes stay visible in the audit history");
      await Assert.That(profile.LatestCommand!.CommandId)
          .IsEqualTo(profile.RecentCommands[0].CommandId);

      var foreignTenant = await store.GetControlsAsync(
          "other-tenant",
          120,
          cancellationToken);
      await Assert.That(foreignTenant)
          .IsEmpty()
          .Because("controls are tenant scoped");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Queue_Populates_Previous_Identity_From_Proven_Prior_Success(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);

      var firstCandidateId = Guid.NewGuid();
      var firstAuthority = new ImageRolloutCandidateAuthority(
          firstCandidateId,
          RecipeId,
          TargetDigest,
          "linux/amd64");
      var firstResult = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          firstAuthority,
          CreateFences(),
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(firstResult.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              firstResult.CommandId!.Value,
              "succeeded",
              null,
              "Applied target digest.",
              TargetDigest,
              TargetWorkerRevision,
              "current",
              4,
              0,
              null,
              Now.AddSeconds(200)),
          Now.AddSeconds(201),
          Now.AddSeconds(150),
          cancellationToken);

      // The capability projected after the first success advertises the
      // applied digest and revision as the currently observed state.
      var afterCapability = CreateCapabilityAfterFirstSuccess();
      var afterReceivedAt = Now.AddMinutes(20);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          afterCapability,
          null,
          null,
          afterReceivedAt,
          afterReceivedAt.AddSeconds(-120),
          cancellationToken);

      var secondCandidateId = Guid.NewGuid();
      var secondAuthority = new ImageRolloutCandidateAuthority(
          secondCandidateId,
          RecipeId,
          SecondTargetDigest,
          "linux/amd64");
      var secondFences = CreateFencesAfterFirstSuccess();
      var secondResult = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          secondAuthority,
          secondFences,
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          afterReceivedAt.AddSeconds(1),
          afterReceivedAt.AddMinutes(10),
          afterReceivedAt.AddSeconds(-120),
          afterReceivedAt.AddSeconds(-60),
          cancellationToken);
      await Assert.That(secondResult.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      var recent = controls[0].Profiles[0].RecentCommands;
      await Assert.That(recent.Count).IsEqualTo(2);
      await Assert.That(recent[0].CommandId)
          .IsEqualTo(secondResult.CommandId!.Value)
          .Because("history is newest first");

      var newest = recent[0];
      await Assert.That(newest.PreviousCandidateId)
          .IsEqualTo(firstCandidateId)
          .Because("the prior candidate authority is derived from a proven success");
      await Assert.That(newest.PreviousRecipeId).IsEqualTo(RecipeId);
      await Assert.That(newest.PreviousImageDigest).IsEqualTo(TargetDigest);
      await Assert.That(newest.PreviousWorkerRevision)
          .IsEqualTo(TargetWorkerRevision);
      await Assert.That(newest.PreviousImageReference)
          .IsEqualTo(afterCapability.Profiles[0].CurrentImageReference);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Queue_Leaves_Previous_Identity_Null_When_Prior_Success_Digest_Matches_But_Worker_Revision_Drifted(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);

      // Queue and apply a first successful rollout at (TargetDigest,
      // TargetWorkerRevision). This row is the only candidate the prior-
      // authority lookup could return.
      var firstCandidateId = Guid.NewGuid();
      var firstAuthority = new ImageRolloutCandidateAuthority(
          firstCandidateId,
          RecipeId,
          TargetDigest,
          "linux/amd64");
      var firstResult = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          firstAuthority,
          CreateFences(),
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(firstResult.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              firstResult.CommandId!.Value,
              "succeeded",
              null,
              "Applied target digest.",
              TargetDigest,
              TargetWorkerRevision,
              "current",
              4,
              0,
              null,
              Now.AddSeconds(200)),
          Now.AddSeconds(201),
          Now.AddSeconds(150),
          cancellationToken);

      // Simulate a post-success non-image profile change: the digest
      // is still applied (TargetDigest), but the worker revision has
      // drifted away from TargetWorkerRevision. The next capability the
      // connector projects shows the drifted revision, so the fences
      // reflect that same drift.
      var driftedCapability = new ImageRolloutOperatorCapability(
          [
              new ImageRolloutOperatorProfile(
                  "default",
                  "linux/amd64",
                  RegistryRepository + "@" + TargetDigest,
                  TargetDigest,
                  TargetDigest,
                  DriftedWorkerRevision,
                  StaticFingerprintAfterSuccess,
                  PreservedFingerprint,
                  RoutingFingerprint,
                  7,
                  DesiredStateHash,
                  [RecipeId],
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
      var driftReceivedAt = Now.AddMinutes(20);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          driftedCapability,
          null,
          null,
          driftReceivedAt,
          driftReceivedAt.AddSeconds(-120),
          cancellationToken);
      var driftedFences = new ImageRolloutCommandFences(
          RegistryRepository + "@" + TargetDigest,
          TargetDigest,
          TargetDigest,
          DriftedWorkerRevision,
          StaticFingerprintAfterSuccess,
          PreservedFingerprint,
          RoutingFingerprint,
          7,
          DesiredStateHash);

      // Queue a second rollout with fences that pin the drifted revision.
      // The store's prior-authority lookup filters on both digest AND
      // revision, so the earlier succeeded row (with TargetWorkerRevision)
      // does not match and no prior authority is persisted.
      var secondCandidateId = Guid.NewGuid();
      var secondAuthority = new ImageRolloutCandidateAuthority(
          secondCandidateId,
          RecipeId,
          SecondTargetDigest,
          "linux/amd64");
      var secondResult = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          secondAuthority,
          driftedFences,
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          driftReceivedAt.AddSeconds(1),
          driftReceivedAt.AddMinutes(10),
          driftReceivedAt.AddSeconds(-120),
          driftReceivedAt.AddSeconds(-60),
          cancellationToken);
      await Assert.That(secondResult.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued)
          .Because(
              "the drifted-revision fence still matches the current "
              + "capability, so the queue itself is accepted");

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      var newest = controls[0].Profiles[0].RecentCommands[0];
      await Assert.That(newest.CommandId)
          .IsEqualTo(secondResult.CommandId!.Value);
      await Assert.That(newest.PreviousCandidateId)
          .IsNull()
          .Because(
              "the prior succeeded row's target_worker_revision does not "
              + "match the drifted current revision, so no prior "
              + "candidate authority can be proved for a later rollback");
      await Assert.That(newest.PreviousRecipeId)
          .IsNull()
          .Because(
              "recipe authority is only trusted when both digest and "
              + "revision fences prove the prior success is still applied");
      await Assert.That(newest.PreviousImageDigest)
          .IsNull()
          .Because(
              "the prior digest is not authoritative on its own once the "
              + "worker revision has drifted away from that success");
      await Assert.That(newest.PreviousWorkerRevision)
          .IsNull()
          .Because(
              "recording the prior worker revision would encode stale "
              + "authority the observed state no longer reflects");
      await Assert.That(newest.PreviousImageReference)
          .IsNull()
          .Because(
              "PreviousImageReference is only set when a prior success "
              + "match is found; drifted revision means no match");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Queue_Leaves_Previous_Identity_Null_For_Unmanaged_Legacy_Image(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      var legacyCapability = CreateLegacyCapability();
      await store.ApplyConnectorSyncAsync(
          nodeId,
          legacyCapability,
          null,
          null,
          Now,
          Now.AddSeconds(-120),
          cancellationToken);

      var legacyFences = new ImageRolloutCommandFences(
          null,
          null,
          null,
          null,
          StaticFingerprint,
          PreservedFingerprint,
          RoutingFingerprint,
          7,
          DesiredStateHash);
      var result = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          CreateCandidateAuthority(),
          legacyFences,
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(result.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      var newest = controls[0].Profiles[0].RecentCommands[0];
      await Assert.That(newest.PreviousCandidateId)
          .IsNull()
          .Because("unmanaged legacy images have no proven prior candidate");
      await Assert.That(newest.PreviousRecipeId).IsNull();
      await Assert.That(newest.PreviousImageDigest).IsNull();
      await Assert.That(newest.PreviousWorkerRevision).IsNull();
      await Assert.That(newest.PreviousImageReference).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Queue_Leaves_Previous_Identity_Null_When_Prior_Rollout_Was_Not_Succeeded(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);

      // A prior rollout targeted the currently observed digest, but ended
      // in failure rather than success. It must not become the source of
      // truth for the prior candidate authority.
      var priorFailedResult = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          new ImageRolloutCandidateAuthority(
              Guid.NewGuid(),
              RecipeId,
              "sha256:1111111111111111111111111111111111111111111111111111111111111111",
              "linux/amd64"),
          CreateFences(),
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(priorFailedResult.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              priorFailedResult.CommandId!.Value,
              "failed",
              "process-failure",
              "Prior rollout failed.",
              null,
              null,
              "degraded",
              null,
              null,
              "boom",
              Now.AddSeconds(200)),
          Now.AddSeconds(201),
          Now.AddSeconds(150),
          cancellationToken);

      var secondCandidateId = Guid.NewGuid();
      var receivedAt = Now.AddMinutes(20);
      await SynchronizeAsync(store, nodeId, receivedAt, cancellationToken);
      var secondResult = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          new ImageRolloutCandidateAuthority(
              secondCandidateId,
              RecipeId,
              SecondTargetDigest,
              "linux/amd64"),
          CreateFences(),
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          receivedAt.AddSeconds(1),
          receivedAt.AddMinutes(10),
          receivedAt.AddSeconds(-120),
          receivedAt.AddSeconds(-60),
          cancellationToken);
      await Assert.That(secondResult.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      var newest = controls[0].Profiles[0].RecentCommands[0];
      await Assert.That(newest.PreviousCandidateId).IsNull();
      await Assert.That(newest.PreviousRecipeId).IsNull();
      await Assert.That(newest.PreviousImageDigest).IsNull();
      await Assert.That(newest.PreviousWorkerRevision).IsNull();
      await Assert.That(newest.PreviousImageReference).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Controls_Exclude_Revoked_Nodes_And_Their_History(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      var beforeRevoke = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(beforeRevoke)
          .HasSingleItem()
          .Because("the enrolled node projects rollout capability");

      // Revoke the node the way the fleet store does — persist a revoked_at
      // marker on the nodes row. The rollout store must then hide both the
      // capability and the immutable command history from aggregate reads.
      await using (var connection = await connectionFactory.OpenAsync(
              cancellationToken))
      await using (var revoke = connection.CreateCommand())
      {
        revoke.CommandText =
            """
            UPDATE nodes
            SET revoked_at = $revokedAt
            WHERE node_id = $nodeId;
            """;
        revoke.Parameters.AddWithValue(
            "$revokedAt",
            Now.AddMinutes(1).ToString("O", CultureInfo.InvariantCulture));
        revoke.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
        await revoke.ExecuteNonQueryAsync(cancellationToken);
      }

      var afterRevoke = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      await Assert.That(afterRevoke)
          .IsEmpty()
          .Because("revoked nodes never project rollout capability or history");

      var singleControl = await store.GetProfileControlOrNullAsync(
          "tenant",
          nodeId,
          "default",
          120,
          cancellationToken);
      await Assert.That(singleControl)
          .IsNull()
          .Because("single-profile reads also exclude revoked nodes");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Idempotency_Key_Returns_Existing_Command_On_Exact_Replay(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var candidate = CreateCandidateAuthority();
      var fences = CreateFences();
      const string key = "replay-key-01";
      const string signature =
          "1111111111111111111111111111111111111111111111111111111111111111";
      var initial = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          candidate,
          fences,
          "1",
          key,
          signature,
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(initial.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);
      await Assert.That(initial.CommandId).IsNotNull();

      var replay = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          candidate,
          fences,
          "1",
          key,
          signature,
          Now.AddSeconds(5),
          Now.AddSeconds(5).AddMinutes(10),
          Now.AddSeconds(5).AddSeconds(-120),
          Now.AddSeconds(5).AddSeconds(-60),
          cancellationToken);
      await Assert.That(replay.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.IdempotentReplay);
      await Assert.That(replay.CommandId).IsEqualTo(initial.CommandId);

      var replayActiveWindow = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          candidate,
          fences,
          "1",
          key,
          signature,
          Now.AddSeconds(15),
          Now.AddSeconds(15).AddMinutes(10),
          Now.AddSeconds(15).AddSeconds(-120),
          Now.AddSeconds(15).AddSeconds(-60),
          cancellationToken);
      await Assert.That(replayActiveWindow.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.IdempotentReplay)
          .Because("replay stays idempotent even while the earlier command is active");
      await Assert.That(replayActiveWindow.CommandId)
          .IsEqualTo(initial.CommandId);

      // Finish the command and confirm the same key still resolves to the
      // same durable identifier while inside the cooldown window.
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              initial.CommandId!.Value,
              "succeeded",
              null,
              "Applied target digest.",
              TargetDigest,
              TargetWorkerRevision,
              "current",
              4,
              0,
              null,
              Now.AddSeconds(200)),
          Now.AddSeconds(201),
          Now.AddSeconds(-60),
          cancellationToken);
      var replayInCooldown = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          candidate,
          fences,
          "1",
          key,
          signature,
          Now.AddSeconds(210),
          Now.AddSeconds(210).AddMinutes(10),
          Now.AddSeconds(210).AddSeconds(-120),
          Now.AddSeconds(210).AddSeconds(-1),
          cancellationToken);
      await Assert.That(replayInCooldown.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.IdempotentReplay)
          .Because("cooldown never suppresses an exact replay");
      await Assert.That(replayInCooldown.CommandId)
          .IsEqualTo(initial.CommandId);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Idempotency_Key_Reuse_With_Different_Authority_Is_A_Stable_Conflict(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      const string key = "conflict-key-01";
      var initial = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          CreateCandidateAuthority(),
          CreateFences(),
          "1",
          key,
          "1111111111111111111111111111111111111111111111111111111111111111",
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(initial.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      var reuse = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          new ImageRolloutCandidateAuthority(
              Guid.NewGuid(),
              RecipeId,
              SecondTargetDigest,
              "linux/amd64"),
          CreateFences(),
          "1",
          key,
          "2222222222222222222222222222222222222222222222222222222222222222",
          Now.AddSeconds(5),
          Now.AddSeconds(5).AddMinutes(10),
          Now.AddSeconds(5).AddSeconds(-120),
          Now.AddSeconds(5).AddSeconds(-60),
          cancellationToken);
      await Assert.That(reuse.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.IdempotencyKeyReuseConflict);
      await Assert.That(reuse.CommandId)
          .IsNull()
          .Because("mismatched key reuse must not leak the earlier command id");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  // The pre-candidate durable replay probe exposed through
  // LookupIdempotentReplayAsync must resolve exact replays and conflicts
  // without touching capability, candidate, fence, or eligibility state.
  // It is the primitive that keeps at-most-once semantics durable when
  // candidate retention later removes the immutable candidate row.
  [Test]
  public async Task LookupIdempotentReplay_Returns_Distinct_Outcomes_By_Signature(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      const string key = "lookup-key-01";
      const string signature =
          "4444444444444444444444444444444444444444444444444444444444444444";
      const string differentSignature =
          "5555555555555555555555555555555555555555555555555555555555555555";

      // No prior command: NoExistingCommand.
      var initialProbe = await store.LookupIdempotentReplayAsync(
          "tenant",
          nodeId,
          "1",
          key,
          signature,
          cancellationToken);
      await Assert.That(initialProbe.Outcome)
          .IsEqualTo(ImageRolloutIdempotencyLookupOutcome.NoExistingCommand)
          .Because("no prior command exists for this actor/key");
      await Assert.That(initialProbe.CommandId).IsNull();

      // Queue one durable command with the given signature.
      var queued = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          CreateCandidateAuthority(),
          CreateFences(),
          "1",
          key,
          signature,
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);
      await Assert.That(queued.CommandId).IsNotNull();

      // Same signature: IdempotentReplay with the durable command id.
      var replayProbe = await store.LookupIdempotentReplayAsync(
          "tenant",
          nodeId,
          "1",
          key,
          signature,
          cancellationToken);
      await Assert.That(replayProbe.Outcome)
          .IsEqualTo(ImageRolloutIdempotencyLookupOutcome.IdempotentReplay);
      await Assert.That(replayProbe.CommandId).IsEqualTo(queued.CommandId);

      // Different signature: IdempotencyKeyReuseConflict with no id.
      var conflictProbe = await store.LookupIdempotentReplayAsync(
          "tenant",
          nodeId,
          "1",
          key,
          differentSignature,
          cancellationToken);
      await Assert.That(conflictProbe.Outcome)
          .IsEqualTo(
              ImageRolloutIdempotencyLookupOutcome
                  .IdempotencyKeyReuseConflict);
      await Assert.That(conflictProbe.CommandId)
          .IsNull()
          .Because("conflict never leaks the earlier command id");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  // Durability regression for the idempotency fix: an exact replay must
  // still resolve to the original durable command even after any
  // candidate retention removes the immutable candidate that produced
  // the command. The store never touches candidates on lookup, so this
  // test simply proves the lookup remains authoritative after arbitrary
  // time and any candidate changes the orchestrator layer might make.
  [Test]
  public async Task LookupIdempotentReplay_Returns_Existing_Command_Regardless_Of_Candidate_State(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      const string key = "lookup-durable-01";
      const string signature =
          "6666666666666666666666666666666666666666666666666666666666666666";
      var queued = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          CreateCandidateAuthority(),
          CreateFences(),
          "1",
          key,
          signature,
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      // A much later replay probe (as would happen from a retry after
      // candidate retention had a chance to prune the source candidate)
      // still returns the durable command id.
      var laterProbe = await store.LookupIdempotentReplayAsync(
          "tenant",
          nodeId,
          "1",
          key,
          signature,
          cancellationToken);
      await Assert.That(laterProbe.Outcome)
          .IsEqualTo(ImageRolloutIdempotencyLookupOutcome.IdempotentReplay);
      await Assert.That(laterProbe.CommandId).IsEqualTo(queued.CommandId);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  // Cross-tenant isolation is enforced at the JOIN nodes ON tenant_id
  // filter, matching the in-transaction lookup in QueueAsync. A foreign
  // tenant reusing the same node id and idempotency key must see
  // NoExistingCommand and never learn that a prior command exists.
  [Test]
  public async Task LookupIdempotentReplay_Rejected_Across_Foreign_Tenant(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      const string key = "foreign-tenant-key-01";
      const string signature =
          "7777777777777777777777777777777777777777777777777777777777777777";
      var queued = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          CreateCandidateAuthority(),
          CreateFences(),
          "1",
          key,
          signature,
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      var foreignProbe = await store.LookupIdempotentReplayAsync(
          "other-tenant",
          nodeId,
          "1",
          key,
          signature,
          cancellationToken);
      await Assert.That(foreignProbe.Outcome)
          .IsEqualTo(ImageRolloutIdempotencyLookupOutcome.NoExistingCommand)
          .Because(
              "the lookup joins nodes on tenant_id so a foreign tenant "
              + "cannot discover an existing command");
      await Assert.That(foreignProbe.CommandId).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Idempotency_Key_Is_Scoped_By_Actor_Not_Global(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);

      // Add a second dashboard user so the second QueueAsync satisfies the
      // requested_by_github_user_id FK.
      await using (var connection = await connectionFactory.OpenAsync(
              cancellationToken))
      await using (var insertUser = connection.CreateCommand())
      {
        insertUser.CommandText =
            """
            INSERT INTO dashboard_users (
                github_user_id,
                github_login,
                display_name,
                first_seen_at,
                last_seen_at)
            VALUES ('2', 'other', 'Other', $seenAt, $seenAt);
            """;
        insertUser.Parameters.AddWithValue(
            "$seenAt",
            Now.ToString("O", CultureInfo.InvariantCulture));
        await insertUser.ExecuteNonQueryAsync(cancellationToken);
      }

      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      const string key = "shared-key-01";
      const string signature =
          "3333333333333333333333333333333333333333333333333333333333333333";
      var firstActor = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          CreateCandidateAuthority(),
          CreateFences(),
          "1",
          key,
          signature,
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(firstActor.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      // A different actor using the SAME key MUST not receive the earlier
      // actor's durable command. It also must not blow up on the unique
      // index: uniqueness is scoped to (node, actor, key). Since we have
      // no other authorization to change, the second attempt will fail
      // downstream with Conflict from the profile operation slot — that
      // is the correct behaviour: idempotency keys never span actors.
      var secondActor = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          CreateCandidateAuthority(),
          CreateFences(),
          "2",
          key,
          signature,
          Now.AddSeconds(5),
          Now.AddSeconds(5).AddMinutes(10),
          Now.AddSeconds(5).AddSeconds(-120),
          Now.AddSeconds(5).AddSeconds(-60),
          cancellationToken);
      await Assert.That(secondActor.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Conflict)
          .Because(
              "distinct actors cannot pretend to be an idempotent replay; "
              + "the second actor collides on the profile-operation slot instead");
      await Assert.That(secondActor.CommandId).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Idempotency_Replay_Ignores_Revoked_Node(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var candidate = CreateCandidateAuthority();
      var fences = CreateFences();
      const string key = "revoked-replay-key";
      const string signature =
          "4444444444444444444444444444444444444444444444444444444444444444";
      var initial = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          candidate,
          fences,
          "1",
          key,
          signature,
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(initial.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      await using (var connection = await connectionFactory.OpenAsync(
              cancellationToken))
      await using (var revoke = connection.CreateCommand())
      {
        revoke.CommandText =
            """
            UPDATE nodes
            SET revoked_at = $revokedAt
            WHERE node_id = $nodeId;
            """;
        revoke.Parameters.AddWithValue(
            "$revokedAt",
            Now.AddSeconds(30).ToString("O", CultureInfo.InvariantCulture));
        revoke.Parameters.AddWithValue("$nodeId", nodeId.ToString("D"));
        await revoke.ExecuteNonQueryAsync(cancellationToken);
      }

      var replayAfterRevoke = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          candidate,
          fences,
          "1",
          key,
          signature,
          Now.AddSeconds(45),
          Now.AddSeconds(45).AddMinutes(10),
          Now.AddSeconds(45).AddSeconds(-120),
          Now.AddSeconds(45).AddSeconds(-60),
          cancellationToken);
      await Assert.That(replayAfterRevoke.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.NodeNotFound)
          .Because(
              "a revoked node must never surface its earlier command "
              + "through a POST replay, even under the same tenant and actor");
      await Assert.That(replayAfterRevoke.CommandId).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Idempotency_Replay_Rejected_Across_Foreign_Tenant(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var candidate = CreateCandidateAuthority();
      var fences = CreateFences();
      const string key = "foreign-replay-key";
      const string signature =
          "5555555555555555555555555555555555555555555555555555555555555555";
      var initial = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          candidate,
          fences,
          "1",
          key,
          signature,
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(initial.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      var owner = new DashboardUser(
          "1",
          "owner",
          "Owner",
          null);
      await new SqliteAccessStore(connectionFactory).EnsureTenantOwnerAsync(
          "other-tenant",
          "Other Tenant",
          owner,
          Now,
          cancellationToken);

      var replayFromForeignTenant = await store.QueueAsync(
          "other-tenant",
          nodeId,
          "default",
          candidate,
          fences,
          "1",
          key,
          signature,
          Now.AddSeconds(5),
          Now.AddSeconds(5).AddMinutes(10),
          Now.AddSeconds(5).AddSeconds(-120),
          Now.AddSeconds(5).AddSeconds(-60),
          cancellationToken);
      await Assert.That(replayFromForeignTenant.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.NodeNotFound)
          .Because(
              "the idempotency lookup joins nodes on tenant_id so a foreign "
              + "tenant never sees the owning tenant's durable command");
      await Assert.That(replayFromForeignTenant.CommandId).IsNull();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Allowed_Recipe_Matching_Is_Case_Insensitive(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var mixedCaseCandidate = new ImageRolloutCandidateAuthority(
          Guid.NewGuid(),
          RecipeId.ToUpperInvariant(),
          TargetDigest,
          "linux/amd64");
      var queued = await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          mixedCaseCandidate,
          CreateFences(),
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          Now,
          Now.AddMinutes(10),
          Now.AddSeconds(-120),
          Now.AddSeconds(-60),
          cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued)
          .Because(
              "recipe IDs must match case-insensitively; the connector and "
              + "installer treat them case-insensitively");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Profile_Control_Read_Is_Foreign_Tenant_Safe(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);

      var owningTenantView = await store.GetProfileControlOrNullAsync(
          "tenant",
          nodeId,
          "default",
          120,
          cancellationToken);
      await Assert.That(owningTenantView)
          .IsNotNull()
          .Because("the owning tenant projects rollout control and history");

      var foreignTenantView = await store.GetProfileControlOrNullAsync(
          "other-tenant",
          nodeId,
          "default",
          120,
          cancellationToken);
      await Assert.That(foreignTenantView)
          .IsNull()
          .Because(
              "single-profile reads never expose data belonging to a "
              + "different tenant, even for a known nodeId+profileId pair");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Success_Outcome_With_Mismatched_Target_Digest_Terminalizes_Indeterminate_Unknown(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);
      await Assert.That(queued.Status)
          .IsEqualTo(ImageRolloutCommandQueueStatus.Queued);
      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);

      await using (var connection = await connectionFactory.OpenAsync(
              cancellationToken))
      await using (var seedRevision = connection.CreateCommand())
      {
        seedRevision.CommandText =
            """
            UPDATE image_rollout_commands
            SET target_worker_revision = $targetWorkerRevision
            WHERE command_id = $commandId;
            """;
        seedRevision.Parameters.AddWithValue(
            "$targetWorkerRevision",
            CurrentWorkerRevision);
        seedRevision.Parameters.AddWithValue(
            "$commandId",
            queued.CommandId!.Value.ToString("D"));
        await seedRevision.ExecuteNonQueryAsync(cancellationToken);
      }

      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              queued.CommandId!.Value,
              "succeeded",
              null,
              "Ignored operator message with a digest we never queued.",
              SecondTargetDigest,
              TargetWorkerRevision,
              "current",
              4,
              0,
              "should-not-persist",
              Now.AddSeconds(30)),
          Now.AddSeconds(31),
          Now.AddSeconds(-60),
          cancellationToken);

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      var command = controls[0].Profiles[0].LatestCommand;
      await Assert.That(command).IsNotNull();
      await Assert.That(command!.Status)
          .IsEqualTo("indeterminate")
          .Because(
              "a success outcome with mismatched target digest can never "
              + "terminalize the command as succeeded");
      await Assert.That(command.FailureCategory)
          .IsEqualTo("unknown")
          .Because(
              "the store terminalizes untrusted success authority as "
              + "unknown so it maps to a closed non-success bucket");
      await Assert.That(command.TargetDigest).IsEqualTo(TargetDigest);
      await Assert.That(command.TargetWorkerRevision)
          .IsNull()
          .Because(
              "the reported worker revision is untrusted when authority "
              + "did not match and must not be persisted");
      await Assert.That(command.CurrentWorkers)
          .IsNull()
          .Because(
              "worker counts are only meaningful for a proven applied "
              + "target and must not be persisted for untrusted success");
      await Assert.That(command.StaleWorkers).IsNull();
      await Assert.That(command.ManagerConvergenceStatus).IsNull();
      await Assert.That(command.LastError)
          .IsNull()
          .Because(
              "untrusted success outcomes do not carry authoritative "
              + "error diagnostics into the store");
      await Assert.That(command.ResultMessage)
          .IsNotNull()
          .And.Contains("did not match")
          .Because(
              "the store persists a bounded fixed literal message and "
              + "never the connector-supplied text or the reported digest");
      await Assert.That(command.ResultMessage!)
          .DoesNotContain(SecondTargetDigest)
          .And.DoesNotContain(TargetDigest)
          .Because(
              "the mismatch message must not leak either the reported or "
              + "queued digest as free-form text");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  // Defense-in-depth: even if the protocol validation is bypassed (for
  // example a store-level test or a future migration path), a success
  // outcome that omits target_worker_revision cannot be trusted. The
  // store terminalizes indeterminate/unknown rather than succeed, so no
  // "succeeded" row can ever exist without a non-null worker revision
  // authority.
  [Test]
  public async Task Success_Outcome_With_Missing_Target_Worker_Revision_Terminalizes_Indeterminate_Unknown(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);
      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);

      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          new ImageRolloutCommandOutcome(
              queued.CommandId!.Value,
              "succeeded",
              null,
              "Applied but did not report a worker revision.",
              TargetDigest,
              null,
              "current",
              4,
              0,
              null,
              Now.AddSeconds(30)),
          Now.AddSeconds(31),
          Now.AddSeconds(-60),
          cancellationToken);

      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      var command = controls[0].Profiles[0].LatestCommand;
      await Assert.That(command).IsNotNull();
      await Assert.That(command!.Status)
          .IsEqualTo("indeterminate")
          .Because(
              "a success outcome missing target_worker_revision cannot "
              + "prove authority and must never terminalize as succeeded");
      await Assert.That(command.FailureCategory)
          .IsEqualTo("unknown");
      await Assert.That(command.TargetWorkerRevision).IsNull();
      await Assert.That(command.ManagerConvergenceStatus).IsNull();
      await Assert.That(command.ResultMessage)
          .IsNotNull()
          .And.Contains("did not match");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  // The database trigger independently enforces terminal evidence even
  // when a caller bypasses the store's authority checks.
  [Test]
  public async Task Succeeded_Row_With_Missing_Evidence_Is_Rejected_By_Terminal_Evidence_Trigger(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath();
    try
    {
      var (connectionFactory, nodeId) = await CreateEnrolledNodeAsync(
          databasePath,
          cancellationToken);
      var store = new SqliteImageRolloutCommandStore(connectionFactory);
      await SynchronizeAsync(store, nodeId, Now, cancellationToken);
      var queued = await QueueAsync(store, nodeId, Now, cancellationToken);
      await SynchronizeAsync(
          store,
          nodeId,
          Now.AddSeconds(1),
          cancellationToken);
      // Progress the command to 'started' so a raw UPDATE to 'succeeded'
      // is a valid lifecycle transition; only the terminal_evidence
      // trigger should block the incomplete evidence shape.
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          new ImageRolloutCommandProgress(
              queued.CommandId!.Value,
              "claimed",
              Now.AddSeconds(10)),
          null,
          Now.AddSeconds(11),
          Now.AddSeconds(-60),
          cancellationToken);
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          new ImageRolloutCommandProgress(
              queued.CommandId!.Value,
              "started",
              Now.AddSeconds(20)),
          null,
          Now.AddSeconds(21),
          Now.AddSeconds(-60),
          cancellationToken);

      // Missing target_worker_revision.
      await Assert.That(
              async () => await ExecuteSucceededUpdateAsync(
                  connectionFactory,
                  queued.CommandId.Value,
                  targetWorkerRevision: null,
                  currentWorkers: 4,
                  staleWorkers: 0,
                  managerConvergenceStatus: "current",
                  cancellationToken))
          .Throws<SqliteException>()
          .Because(
              "the trigger must reject a succeeded row without a target "
              + "worker revision so no untrusted authority can slip in");

      // Missing current_workers.
      await Assert.That(
              async () => await ExecuteSucceededUpdateAsync(
                  connectionFactory,
                  queued.CommandId.Value,
                  targetWorkerRevision: TargetWorkerRevision,
                  currentWorkers: null,
                  staleWorkers: 0,
                  managerConvergenceStatus: "current",
                  cancellationToken))
          .Throws<SqliteException>()
          .Because(
              "the trigger must reject a succeeded row without observed "
              + "current worker counts");

      // Missing stale_workers.
      await Assert.That(
              async () => await ExecuteSucceededUpdateAsync(
                  connectionFactory,
                  queued.CommandId.Value,
                  targetWorkerRevision: TargetWorkerRevision,
                  currentWorkers: 4,
                  staleWorkers: null,
                  managerConvergenceStatus: "current",
                  cancellationToken))
          .Throws<SqliteException>()
          .Because(
              "the trigger must reject a succeeded row without observed "
              + "stale worker counts");

      // Missing manager_convergence_status.
      await Assert.That(
              async () => await ExecuteSucceededUpdateAsync(
                  connectionFactory,
                  queued.CommandId.Value,
                  targetWorkerRevision: TargetWorkerRevision,
                  currentWorkers: 4,
                  staleWorkers: 0,
                  managerConvergenceStatus: null,
                  cancellationToken))
          .Throws<SqliteException>()
          .Because(
              "the trigger must reject a succeeded row without a bounded "
              + "manager convergence status");

      // The row must remain 'started' — none of the failing UPDATEs
      // partially applied. Read back via the store to confirm.
      var controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      var command = controls[0].Profiles[0].LatestCommand;
      await Assert.That(command).IsNotNull();
      await Assert.That(command!.Status)
          .IsEqualTo("started")
          .Because(
              "every rejected UPDATE is aborted and leaves the row in "
              + "its pre-transition lifecycle status");

      // A complete succeeded UPDATE (all four evidence fields non-null)
      // is accepted, proving the trigger only rejects the incomplete
      // shape and not the shape as such.
      await ExecuteSucceededUpdateAsync(
          connectionFactory,
          queued.CommandId.Value,
          targetWorkerRevision: TargetWorkerRevision,
          currentWorkers: 4,
          staleWorkers: 0,
          managerConvergenceStatus: "current",
          cancellationToken);
      controls = await store.GetControlsAsync(
          "tenant",
          120,
          cancellationToken);
      command = controls[0].Profiles[0].LatestCommand;
      await Assert.That(command!.Status).IsEqualTo("succeeded");
      await Assert.That(command.TargetWorkerRevision)
          .IsEqualTo(TargetWorkerRevision);
      await Assert.That(command.CurrentWorkers).IsEqualTo(4);
      await Assert.That(command.StaleWorkers).IsEqualTo(0);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static string CreateDatabasePath() =>
      Path.Combine(
          Path.GetTempPath(),
          $"pitcrew-rollout-{Guid.NewGuid():N}.db");

  private static ImageRolloutOperatorCapability CreateCapability() =>
      new(
          [
              new ImageRolloutOperatorProfile(
                  "default",
                  "linux/amd64",
                  "ghcr.io/example/runner:main",
                  "sha256:1111111111111111111111111111111111111111111111111111111111111111",
                  "sha256:2222222222222222222222222222222222222222222222222222222222222222",
                  CurrentWorkerRevision,
                  StaticFingerprint,
                  PreservedFingerprint,
                  RoutingFingerprint,
                  7,
                  DesiredStateHash,
                  [RecipeId],
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

  private static ImageRolloutCandidateAuthority CreateCandidateAuthority() =>
      new(
          Guid.NewGuid(),
          RecipeId,
          TargetDigest,
          "linux/amd64");

  private static ImageRolloutCommandFences CreateFences() =>
      new(
          "ghcr.io/example/runner:main",
          "sha256:1111111111111111111111111111111111111111111111111111111111111111",
          "sha256:2222222222222222222222222222222222222222222222222222222222222222",
          CurrentWorkerRevision,
          StaticFingerprint,
          PreservedFingerprint,
          RoutingFingerprint,
          7,
          DesiredStateHash);

  // Represents the connector projection after the first successful rollout:
  // the applied target digest and worker revision are now the currently
  // observed state, and the reconstructed manifest is exposed as the
  // canonical registry@digest reference.
  private static ImageRolloutOperatorCapability CreateCapabilityAfterFirstSuccess() =>
      new(
          [
              new ImageRolloutOperatorProfile(
                  "default",
                  "linux/amd64",
                  RegistryRepository + "@" + TargetDigest,
                  TargetDigest,
                  TargetDigest,
                  TargetWorkerRevision,
                  StaticFingerprintAfterSuccess,
                  PreservedFingerprint,
                  RoutingFingerprint,
                  7,
                  DesiredStateHash,
                  [RecipeId],
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

  private static ImageRolloutCommandFences CreateFencesAfterFirstSuccess() =>
      new(
          RegistryRepository + "@" + TargetDigest,
          TargetDigest,
          TargetDigest,
          TargetWorkerRevision,
          StaticFingerprintAfterSuccess,
          PreservedFingerprint,
          RoutingFingerprint,
          7,
          DesiredStateHash);

  // Represents an unmanaged legacy image the connector has never rolled
  // out itself: every currently observed image field is unknown, so no
  // prior candidate authority can be proved.
  private static ImageRolloutOperatorCapability CreateLegacyCapability() =>
      new(
          [
              new ImageRolloutOperatorProfile(
                  "default",
                  "linux/amd64",
                  null,
                  null,
                  null,
                  null,
                  StaticFingerprint,
                  PreservedFingerprint,
                  RoutingFingerprint,
                  7,
                  DesiredStateHash,
                  [RecipeId],
                  true,
                  true,
                  null,
                  false,
                  30,
                  600,
                  1800,
                  "current",
                  null,
                  null),
          ]);

  private static async Task<ImageRolloutCommandQueueResult> QueueAsync(
      SqliteImageRolloutCommandStore store,
      Guid nodeId,
      DateTimeOffset requestedAt,
      CancellationToken cancellationToken) =>
      await store.QueueAsync(
          "tenant",
          nodeId,
          "default",
          CreateCandidateAuthority(),
          CreateFences(),
          "1",
          NewIdempotencyKey(),
          NewIdempotencySignature(),
          requestedAt,
          requestedAt.AddMinutes(10),
          requestedAt.AddSeconds(-120),
          requestedAt.AddSeconds(-60),
          cancellationToken);

  private static string NewIdempotencyKey() =>
      Guid.NewGuid().ToString("N");

  // Test-only stub signature. Production callers use
  // RollOutProfileImageUnitOfWork.ComputeSignature; the store only cares
  // that the value is exactly 64 lowercase hex characters.
  private static string NewIdempotencySignature() =>
      Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

  private static async Task<RollOutProfileImageCommand?> SynchronizeAsync(
      SqliteImageRolloutCommandStore store,
      Guid nodeId,
      DateTimeOffset receivedAt,
      CancellationToken cancellationToken) =>
      await store.ApplyConnectorSyncAsync(
          nodeId,
          CreateCapability(),
          null,
          null,
          receivedAt,
          receivedAt.AddSeconds(-120),
          cancellationToken);

  private static async Task ExecuteAsync(
      SqliteConnectionFactory connectionFactory,
      string sql,
      Guid commandId,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$commandId", commandId.ToString("D"));
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  // Issues a raw UPDATE that transitions a started row to 'succeeded'
  // with the four fields the terminal_evidence trigger asserts non-null
  // for a succeeded row. Any single null in the succeeded-only fields
  // must be aborted by the trigger, not silently persisted.
  private static async Task ExecuteSucceededUpdateAsync(
      SqliteConnectionFactory connectionFactory,
      Guid commandId,
      string? targetWorkerRevision,
      int? currentWorkers,
      int? staleWorkers,
      string? managerConvergenceStatus,
      CancellationToken cancellationToken)
  {
    await using var connection = await connectionFactory.OpenAsync(
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        UPDATE image_rollout_commands
        SET status = 'succeeded',
            failure_category = NULL,
            completed_at = $completedAt,
            target_worker_revision = $targetWorkerRevision,
            current_workers = $currentWorkers,
            stale_workers = $staleWorkers,
            manager_convergence_status = $managerConvergenceStatus,
            last_error = NULL,
            result_message = 'trigger evidence probe'
        WHERE command_id = $commandId;
        """;
    command.Parameters.AddWithValue("$commandId", commandId.ToString("D"));
    command.Parameters.AddWithValue(
        "$completedAt",
        Now.AddSeconds(30).ToString("O"));
    command.Parameters.AddWithValue(
        "$targetWorkerRevision",
        (object?)targetWorkerRevision ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$currentWorkers",
        (object?)currentWorkers ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$staleWorkers",
        (object?)staleWorkers ?? DBNull.Value);
    command.Parameters.AddWithValue(
        "$managerConvergenceStatus",
        (object?)managerConvergenceStatus ?? DBNull.Value);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private static async Task<(
      SqliteConnectionFactory ConnectionFactory,
      Guid NodeId)> CreateEnrolledNodeAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var connectionFactory = new SqliteConnectionFactory(
        Options.Create(new SqliteFleetStoreOptions
        {
          DatabasePath = databasePath,
        }));
    await new SqliteMigrationRunner(connectionFactory).ApplyAsync(
        cancellationToken);
    var owner = new DashboardUser(
        "1",
        "owner",
        "Owner",
        null);
    await new SqliteAccessStore(connectionFactory).EnsureTenantOwnerAsync(
        "tenant",
        "Tenant",
        owner,
        Now,
        cancellationToken);
    var store = new SqliteFleetStore(connectionFactory);
    const string codeHash = "rollout-code-hash";
    await store.CreateEnrollmentCodeAsync(
        Guid.NewGuid(),
        "tenant",
        codeHash,
        "Enrollment",
        owner.GitHubUserId,
        Now,
        Now.AddMinutes(10),
        cancellationToken);
    var enrollment = await store.RedeemEnrollmentCodeAsync(
        codeHash,
        "connector-instance",
        "Connector name",
        "credential-hash",
        Now,
        cancellationToken);
    var nodeId = enrollment.NodeId ??
        throw new InvalidOperationException(
            "Enrollment did not return a node ID.");
    return (connectionFactory, nodeId);
  }
}
