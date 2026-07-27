namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class RecoveryProfileResolverTests
{
  [Test]
  public async Task ReadCapabilityAsync_Advertises_Allowlisted_Healthy_Profile(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-1",
          cancellationToken);
      var resolver = ConnectorTestFactory.CreateRecoveryResolver(
          RecoveryTestData.CreateRecoveryOptions(root),
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles).HasSingleItem();
      var profile = capability.Profiles[0];
      await Assert.That(profile.ProfileId).IsEqualTo("default");
      await Assert.That(profile.ExpectedManagerInstanceId)
          .IsEqualTo("manager-1");
      await Assert.That(profile.DesiredGeneration).IsEqualTo(7);
      await Assert.That(profile.DesiredStateHash)
          .IsEqualTo(RecoveryTestData.DesiredStateHash);
      await Assert.That(profile.ManagerContractSupported).IsTrue();
      await Assert.That(profile.SingleManagerResolved).IsTrue();
      await Assert.That(profile.RecoveryAllowed).IsTrue();
      await Assert.That(profile.OperationActive).IsFalse();
      await Assert.That(profile.ObservedStateAgeSeconds).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Advertises_Nothing_When_Disabled(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-1",
          cancellationToken);
      var options = RecoveryTestData.CreateRecoveryOptions(root);
      options.ManagerRecoveryEnabled = false;
      var resolver = ConnectorTestFactory.CreateRecoveryResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

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
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-1",
          cancellationToken);
      var resolver = ConnectorTestFactory.CreateRecoveryResolver(
          RecoveryTestData.CreateRecoveryOptions(root),
          new FakeHostExecutionEnvironment(true),
          new FixedTimeProvider(RecoveryTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNull();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Omits_Profile_With_Shutdown_Request(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-1",
          cancellationToken);
      RecoveryTestData.WriteShutdownRequest(root);
      var resolver = ConnectorTestFactory.CreateRecoveryResolver(
          RecoveryTestData.CreateRecoveryOptions(root),
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability).IsNotNull();
      await Assert.That(capability!.Profiles).IsEmpty();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Reports_Unsupported_Manager_Contract(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteObservedStateAsync(
          root,
          "manager-1",
          7,
          8,
          "running",
          RecoveryTestData.Now,
          cancellationToken);
      var resolver = ConnectorTestFactory.CreateRecoveryResolver(
          RecoveryTestData.CreateRecoveryOptions(root),
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability!.Profiles).HasSingleItem();
      await Assert.That(capability.Profiles[0].ManagerContractSupported)
          .IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Reports_Stopped_Manager_As_Unresolved(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteObservedStateAsync(
          root,
          "manager-1",
          7,
          9,
          "stopped",
          RecoveryTestData.Now,
          cancellationToken);
      var resolver = ConnectorTestFactory.CreateRecoveryResolver(
          RecoveryTestData.CreateRecoveryOptions(root),
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability!.Profiles).HasSingleItem();
      await Assert.That(capability.Profiles[0].SingleManagerResolved).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Omits_Profile_Outside_Allowlist(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-1",
          cancellationToken);
      var options = RecoveryTestData.CreateRecoveryOptions(root);
      options.AllowedManagerRecoveryProfiles = ["other"];
      var resolver = ConnectorTestFactory.CreateRecoveryResolver(
          options,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(capability!.Profiles).IsEmpty();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ReadCapabilityAsync_Reports_Active_Local_Operation(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-1",
          cancellationToken);
      var options = RecoveryTestData.CreateRecoveryOptions(root);
      var gate = ConnectorTestFactory.CreateOperationGate(options);
      var resolver = ConnectorTestFactory.CreateRecoveryResolver(
          options,
          gate,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));
      using var lease = gate.AcquireOrNull("default");

      var capability = await resolver.ReadCapabilityAsync(cancellationToken);

      await Assert.That(lease).IsNotNull();
      await Assert.That(capability!.Profiles).HasSingleItem();
      await Assert.That(capability.Profiles[0].OperationActive).IsTrue();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }
}
