using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ImageRolloutProfileResolverTests
{
  [Test]
  public async Task ReadCapabilityAsync_Advertises_Nothing_When_Disabled(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      options.ImageRolloutEnabled = false;
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNull();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Advertises_Nothing_In_Container_Mode(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(true),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNull();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Ignores_Profiles_Not_In_Allowlist(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken,
          profileId: "unlisted");
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles).HasSingleItem();
      await Assert.That(capability.Profiles[0].ProfileId)
          .IsEqualTo(ImageRolloutTestData.DefaultProfileId);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Reports_Unsupported_Manager_For_Legacy_Contract(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken,
          managerContractVersion: 15);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles).HasSingleItem();
      var profile = capability.Profiles[0];
      await Assert.That(profile.LocalSchemaSupported).IsFalse();
      await Assert.That(profile.RolloutAllowed).IsFalse();
      await Assert.That(profile.LocalFailureCategory)
          .IsEqualTo("unsupported-manager");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Advertises_Healthy_Rollout_Ready_Profile(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles).HasSingleItem();
      var profile = capability.Profiles[0];
      await Assert.That(profile.ProfileId)
          .IsEqualTo(ImageRolloutTestData.DefaultProfileId);
      await Assert.That(profile.Architecture).IsEqualTo("linux/amd64");
      await Assert.That(profile.CurrentImageReference)
          .IsEqualTo("ghcr.io/example/runner:main");
      // Tag-only references expose no digest; upstream configuration has no
      // separate imageDigest property. Only digest-qualified references
      // (`repo@sha256:...`) surface a current image digest.
      await Assert.That(profile.CurrentImageDigest).IsNull();
      await Assert.That(profile.CurrentLocalImageId)
          .IsEqualTo(ImageRolloutTestData.CurrentLocalImageId);
      await Assert.That(profile.CurrentWorkerRevision)
          .IsEqualTo(ImageRolloutTestData.CurrentWorkerRevision);
      await Assert.That(profile.DesiredGeneration).IsEqualTo(7);
      await Assert.That(profile.DesiredStateHash)
          .IsEqualTo(ImageRolloutTestData.DesiredStateHash);
      await Assert.That(profile.StaticFingerprint)
          .IsEqualTo(ImageRolloutTestData.StaticFingerprint);
      await Assert.That(profile.LocalSchemaSupported).IsTrue();
      await Assert.That(profile.RolloutAllowed).IsTrue();
      await Assert.That(profile.LocalFailureCategory).IsNull();
      await Assert.That(profile.OperationActive).IsFalse();
      await Assert.That(profile.AllowedRecipeIds).HasSingleItem();
      await Assert.That(profile.AllowedRecipeIds[0])
          .IsEqualTo(ImageRolloutTestData.DefaultRecipeId);
      await Assert.That(profile.ObservedStateAgeSeconds).IsEqualTo(0);
      await Assert.That(profile.CommandTimeoutSeconds).IsEqualTo(600);
      await Assert.That(profile.MaximumExpirySeconds).IsEqualTo(1_800);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Uses_Arm64_When_Configured_Label_Requests_Arm64(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken,
          architectureLabel: "linux-arm64",
          architecture: "arm64");
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability!.Profiles[0].Architecture)
          .IsEqualTo("linux/arm64");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Contract: no host.hardware.architecture → the rollout resolver
  // returns no capability row for that profile at all. Publishing a
  // placeholder architecture on the wire would misrepresent an
  // unsupported (or arm64) host to the manager and could enable a
  // rollout that the host cannot safely apply.
  [Test]
  public async Task ReadCapabilityAsync_Fails_Closed_When_Host_Architecture_Missing(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteObservedWithMissingArchitectureAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNotNull();
      // No profile capability is published; the connector logs the
      // closed unsupported-architecture category and continues.
      await Assert.That(capability!.Profiles.Count)
          .IsEqualTo(0)
          .Because(
              "Unsupported/unknown architecture must never advertise a " +
              "placeholder linux/amd64 on the wire.");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 1: fail closed when observedAt is missing.
  [Test]
  public async Task ReadCapabilityAsync_Reports_Stale_Observed_State_When_ObservedAt_Missing(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteObservedWithMissingObservedAtAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles).HasSingleItem();
      var profile = capability.Profiles[0];
      await Assert.That(profile.LocalFailureCategory)
          .IsEqualTo("stale-observed-state");
      await Assert.That(profile.RolloutAllowed).IsFalse();
      // Bounded sentinel (86_400 seconds = 24 hours) keeps the capability
      // payload protocol-valid (age is bounded to [0, 86_400]) while remaining
      // guaranteed above every configured freshness ceiling (local cap 3600,
      // dashboard capability freshness capped well below 86_400).
      await Assert.That(profile.ObservedStateAgeSeconds)
          .IsEqualTo(86_400);
      await Assert.That(profile.LocalSchemaSupported).IsTrue();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 1: fail closed when observedAt cannot be parsed.
  [Test]
  public async Task ReadCapabilityAsync_Reports_Stale_Observed_State_When_ObservedAt_Malformed(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteObservedWithMissingObservedAtAsync(
          root,
          cancellationToken,
          malformedObservedAt: "not-a-timestamp");
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      var profile = capability!.Profiles[0];
      await Assert.That(profile.LocalFailureCategory)
          .IsEqualTo("stale-observed-state");
      await Assert.That(profile.RolloutAllowed).IsFalse();
      await Assert.That(profile.ObservedStateAgeSeconds)
          .IsEqualTo(86_400);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 9: exercise the non-null worker branches (current, rolling).
  [Test]
  public async Task ReadCapabilityAsync_Reports_Current_Convergence_When_Stale_Workers_Zero(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteObservedWorkersAsync(
          root,
          currentWorkers: 4,
          staleWorkers: 0,
          cancellationToken,
          observedAt: ImageRolloutTestData.Now,
          status: "current");
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      var profile = capability!.Profiles[0];
      await Assert.That(profile.ManagerConvergenceStatus).IsEqualTo("current");
      await Assert.That(profile.CurrentWorkers).IsEqualTo(4);
      await Assert.That(profile.StaleWorkers).IsEqualTo(0);
      await Assert.That(profile.LocalFailureCategory).IsNull();
      await Assert.That(profile.RolloutAllowed).IsTrue();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 9: exercise the rolling convergence branch.
  [Test]
  public async Task ReadCapabilityAsync_Reports_Rolling_Convergence_When_Stale_Workers_Positive(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteObservedWorkersAsync(
          root,
          currentWorkers: 2,
          staleWorkers: 2,
          cancellationToken,
          observedAt: ImageRolloutTestData.Now);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      var profile = capability!.Profiles[0];
      await Assert.That(profile.ManagerConvergenceStatus).IsEqualTo("rolling");
      await Assert.That(profile.CurrentWorkers).IsEqualTo(2);
      await Assert.That(profile.StaleWorkers).IsEqualTo(2);
      await Assert.That(profile.RolloutAllowed).IsTrue();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 9: hyphenated recipe IDs and case-insensitive uniqueness both hold.
  [Test]
  public async Task ReadCapabilityAsync_Advertises_Recipe_Ids_With_Hyphens_Ordered_Case_Insensitively(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      options.ImageRolloutRecipes = new List<ImageRolloutRecipePolicyEntry>
      {
        new()
        {
          RecipeId = "beta-recipe",
          RegistryRepository = "ghcr.io/example/beta",
        },
        new()
        {
          RecipeId = "alpha-recipe",
          RegistryRepository = "ghcr.io/example/alpha",
        },
      };
      options.AllowedImageRolloutProfiles =
          [ImageRolloutTestData.DefaultProfileId];
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      var profile = capability!.Profiles[0];
      await Assert.That(profile.AllowedRecipeIds)
          .IsEquivalentTo(new[] { "alpha-recipe", "beta-recipe" });
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Observed update object is required. Missing update makes the
  // observation unauthoritative for every rollout gate, so the resolver
  // must classify the profile as stale-observed-state rather than
  // treating the observation as degraded fallback.
  [Test]
  public async Task
      ReadCapabilityAsync_Reports_Stale_Observed_State_When_Update_Missing(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteObservedWithMissingUpdateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles).HasSingleItem();
      var profile = capability.Profiles[0];
      await Assert.That(profile.LocalFailureCategory)
          .IsEqualTo("stale-observed-state");
      await Assert.That(profile.RolloutAllowed).IsFalse();
      await Assert.That(profile.ManagerConvergenceStatus)
          .IsEqualTo("degraded");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // The observed update.target identity describes the static target for
  // every reported status (current, rolling, degraded). A contradictory
  // target under status='rolling' still invalidates the observation and
  // must be treated as stale.
  [Test]
  public async Task
      ReadCapabilityAsync_Reports_Stale_When_Rolling_Update_Target_Contradicts_Configuration(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteObservedWithMismatchedRollingTargetAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles).HasSingleItem();
      var profile = capability.Profiles[0];
      await Assert.That(profile.LocalFailureCategory)
          .IsEqualTo("stale-observed-state");
      await Assert.That(profile.RolloutAllowed).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }
}
