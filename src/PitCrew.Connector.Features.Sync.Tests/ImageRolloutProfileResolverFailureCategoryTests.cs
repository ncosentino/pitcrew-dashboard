using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

// Focused tests for the closed failure-category vocabulary the profile
// resolver publishes: unsupported-topology (routing/scope), and
// stale-observed-state (observed managerContractVersion missing or
// mismatched with applied static configuration). Split from
// ImageRolloutProfileResolverTests.cs to keep both files under the
// repo test-file line ceiling.
public sealed class ImageRolloutProfileResolverFailureCategoryTests
{
  // ProjectRouting must fail closed (unsupported-schema surface) on
  // malformed repository entries (negative worker counts, duplicate
  // canonical URLs, missing url) instead of silently skipping the entry.
  // The resolver-level surface: an unsupported-schema failure category
  // and a rollout that is not allowed.
  [Test]
  public async Task
      ReadCapabilityAsync_Rejects_Malformed_Repositories_Instead_Of_Skipping(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteDesiredWithMalformedRepositoriesAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      // Malformed routing yields no advertised capability for the
      // profile at all (the connector logs unsupported-schema and skips).
      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles.Count).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Unsupported scope must fail closed at the resolver, not be coerced
  // into paused/empty state that the manager might interpret as safe.
  [Test]
  public async Task
      ReadCapabilityAsync_Rejects_Unsupported_Scope_Instead_Of_Coercing(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteDesiredWithUnsupportedScopeAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles.Count).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Routing-projection failures must classify as unsupported-topology
  // (distinct closed category), not the generic unsupported-schema.
  // Operators and higher-level readers can then tell a topology
  // rejection apart from a schema/manager rejection.
  [Test]
  public async Task
      ResolveAsync_Malformed_Repositories_Classifies_As_Unsupported_Topology(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteDesiredWithMalformedRepositoriesAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var resolution = await resolver.ResolveAsync(
          ImageRolloutTestData.DefaultProfileId,
          cancellationToken);

      await Assert.That(resolution.Profile).IsNull();
      await Assert.That(resolution.FailureCategory)
          .IsEqualTo("unsupported-topology")
          .Because(
              "malformed repositories are a topology failure and must be "
              + "distinguishable from schema/manager failures on the wire");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Unsupported scope in the desired document must also classify as
  // unsupported-topology, not unsupported-schema.
  [Test]
  public async Task
      ResolveAsync_Unsupported_Scope_Classifies_As_Unsupported_Topology(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData.WriteDesiredWithUnsupportedScopeAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var resolver = ConnectorTestFactory.CreateImageRolloutResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var resolution = await resolver.ResolveAsync(
          ImageRolloutTestData.DefaultProfileId,
          cancellationToken);

      await Assert.That(resolution.Profile).IsNull();
      await Assert.That(resolution.FailureCategory)
          .IsEqualTo("unsupported-topology")
          .Because(
              "unsupported scope is a topology (routing) failure and must "
              + "be distinguishable from schema/manager failures on the wire");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Observed managerContractVersion is required so the resolver can verify
  // the observation was produced by the same manager version the applied
  // static configuration describes. When it is missing the resolver must
  // treat the observation as stale rather than accepting an unverified
  // desired-state acknowledgement.
  [Test]
  public async Task
      ReadCapabilityAsync_Reports_Stale_When_Observed_Manager_Contract_Version_Missing(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData
          .WriteObservedWithMissingManagerContractVersionAsync(
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

  // A divergent observed managerContractVersion means the observation was
  // produced by a manager the applied static state does not reflect. The
  // resolver must not silently trust the acknowledged desired-state hash
  // or convergence counts in that case.
  [Test]
  public async Task
      ReadCapabilityAsync_Reports_Stale_When_Observed_Manager_Contract_Version_Mismatched(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      await ImageRolloutTestData
          .WriteObservedWithMismatchedManagerContractVersionAsync(
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
