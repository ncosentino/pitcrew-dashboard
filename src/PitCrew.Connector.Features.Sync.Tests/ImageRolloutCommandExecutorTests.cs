using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ImageRolloutCommandExecutorTests
{
  private const string TargetDigest =
      "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

  private static readonly string TargetWorkerRevision = new('b', 64);
  private static readonly string TargetStaticFingerprint = new('c', 64);

  [Test]
  public async Task ExecuteAsync_Rejects_Command_When_Rollout_Is_Disabled(
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
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("not-allowed");
      await Assert.That(process.InvocationCount).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_Command_When_Expired(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var expired = CreateFencedCommand(capability!) with
      {
        ExpiresAt = ImageRolloutTestData.Now.AddMinutes(-1),
      };

      var report = await executor.ExecuteAsync(expired, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("expired");
      await Assert.That(process.InvocationCount).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_Command_With_Unallowlisted_Recipe(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!) with
      {
        RecipeId = "unlisted-recipe",
      };

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("recipe-not-allowed");
      await Assert.That(process.InvocationCount).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Recipe id fails the local RecipeId shape/format check → the executor
  // must emit the distinct recipe-not-allowed category, not the general
  // not-allowed bucket. Registry repository is not exposed in the outcome.
  [Test]
  public async Task
      ExecuteAsync_Rejects_Command_With_Invalid_Recipe_Id_Shape(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!) with
      {
        // An obviously invalid recipe id shape → hits the format guard.
        RecipeId = "@@invalid@@",
      };

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory)
          .IsEqualTo("recipe-not-allowed");
      await Assert.That(process.InvocationCount).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Recipe entry exists but the local RegistryRepository is invalid at
  // execution time → distinct registry-not-allowed category. The
  // registry repository value must not appear in the outcome message.
  [Test]
  public async Task
      ExecuteAsync_Rejects_Command_With_Invalid_Registry_Repository(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      // Capability is read from valid options; the executor uses a
      // separate options snapshot where the registry repository is
      // invalidated to simulate a policy drift after capability write.
      var capabilityOptions = ImageRolloutTestData.CreateOperatorOptions(
          root);
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              capabilityOptions,
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);

      var executorOptions = ImageRolloutTestData.CreateOperatorOptions(
          root);
      // Wipe the registry repository so IsValidRegistryRepository fails.
      // The recipe id itself is still valid, so this exercises the
      // registry-not-allowed branch specifically (not recipe-not-allowed).
      executorOptions.ImageRolloutRecipes =
          [
              new()
              {
                RecipeId = ImageRolloutTestData.DefaultRecipeId,
                RegistryRepository = string.Empty,
              },
          ];
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          executorOptions,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory)
          .IsEqualTo("registry-not-allowed")
          .Because(
              "an invalid local registry repository is distinct from a "
              + "recipe policy rejection; the exact category preserves "
              + "operator diagnostics without exposing the repo value");
      // Never expose the registry repository or a raw error message.
      var lastError = report.Outcome.LastError ?? string.Empty;
      await Assert.That(lastError)
          .DoesNotContain(ImageRolloutTestData.DefaultRegistryRepository);
      await Assert.That(process.InvocationCount).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_Command_When_Fences_Do_Not_Match(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!) with
      {
        ExpectedDesiredGeneration = 999,
      };

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("stale-fence");
      await Assert.That(process.InvocationCount).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_Command_With_Wrong_Platform(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!) with
      {
        TargetPlatform = "linux/arm64",
      };

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory)
          .IsEqualTo("unsupported-architecture");
      await Assert.That(process.InvocationCount).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Invokes_Setup_Runner_Once_On_Success(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, token) =>
        {
          await WriteConvergedStateAsync(root, token);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(1);
      await Assert.That(report.Progress).IsNotNull();
      await Assert.That(report.Progress!.Phase).IsEqualTo("started");
      await Assert.That(report.Outcome.Status).IsEqualTo("succeeded");
      await Assert.That(report.Outcome.FailureCategory).IsNull();
      await Assert.That(report.Outcome.TargetDigest).IsEqualTo(TargetDigest);
      await Assert.That(report.Outcome.TargetWorkerRevision)
          .IsEqualTo(TargetWorkerRevision);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Builds_Setup_Runner_Arguments_With_ProfilePath(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, token) =>
        {
          await WriteConvergedStateAsync(root, token);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(process.LastRequest).IsNotNull();
      var args = process.LastRequest!.Arguments;
      // Fixed prefix: -NoProfile, -File, Setup-Runner.ps1, -Profile, <id>,
      // -ProfilePath, <manifest>, -NamePrefix <value> (schema-rejected
      // property preserved via CLI), then the local routing shape (in
      // this healthy fixture: -Scope repo -AddRepos <url>=<count>).
      await Assert.That(args.Count).IsEqualTo(13);
      await Assert.That(args[0]).IsEqualTo("-NoProfile");
      await Assert.That(args[1]).IsEqualTo("-File");
      await Assert.That(args[2]).IsEqualTo("Setup-Runner.ps1");
      await Assert.That(args[3]).IsEqualTo("-Profile");
      await Assert.That(args[4]).IsEqualTo(ImageRolloutTestData.DefaultProfileId);
      await Assert.That(args[5]).IsEqualTo("-ProfilePath");
      await Assert.That(args[6])
          .Contains($"{command.CommandId:N}.json");
      await Assert.That(args[7]).IsEqualTo("-NamePrefix");
      await Assert.That(args[8]).IsEqualTo("runner");
      await Assert.That(args[9]).IsEqualTo("-Scope");
      await Assert.That(args[10]).IsEqualTo("repo");
      await Assert.That(args[11]).IsEqualTo("-AddRepos");
      await Assert.That(args[12])
          .IsEqualTo($"{ImageRolloutTestData.DefaultRepositoryUrl}=" +
              $"{ImageRolloutTestData.DefaultRepositoryWorkers}");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Suppresses_Duplicate_Command_On_Replay(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, token) =>
        {
          await WriteConvergedStateAsync(root, token);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var first = await executor.ExecuteAsync(command, cancellationToken);
      var second = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(1);
      await Assert.That(first.Outcome.Status).IsEqualTo("succeeded");
      await Assert.That(second.Outcome.Status).IsEqualTo("succeeded");
      await Assert.That(second.Outcome.CommandId).IsEqualTo(command.CommandId);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ResolveInterruptedAsync_Reports_Failed_When_Preop_State_Unchanged(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner
      {
        Handler = (_, _) => throw new InvalidOperationException(
            "process should not run when the caller injects a started ledger entry"),
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);
      var startedEntry = CreateStartedLedgerEntry(command, capability!);
      await ledger.RecordStartedAsync(startedEntry, cancellationToken);

      var outcomes = await executor.ResolveInterruptedAsync(cancellationToken);

      await Assert.That(outcomes).HasSingleItem();
      await Assert.That(outcomes[0].Status).IsEqualTo("failed");
      await Assert.That(outcomes[0].CommandId).IsEqualTo(command.CommandId);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ResolveInterruptedAsync_Reports_Succeeded_When_Target_Applied(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner
      {
        Handler = (_, _) => throw new InvalidOperationException(
            "process should not run when the caller injects a started ledger entry"),
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);
      var startedEntry = CreateStartedLedgerEntry(command, capability!);
      await ledger.RecordStartedAsync(startedEntry, cancellationToken);
      await WriteConvergedStateAsync(root, cancellationToken);

      var outcomes = await executor.ResolveInterruptedAsync(cancellationToken);

      await Assert.That(outcomes).HasSingleItem();
      await Assert.That(outcomes[0].Status).IsEqualTo("succeeded");
      await Assert.That(outcomes[0].FailureCategory).IsNull();
      await Assert.That(outcomes[0].CommandId).IsEqualTo(command.CommandId);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  private static RollOutProfileImageCommand CreateFencedCommand(
      ImageRolloutOperatorCapability capability)
  {
    var profile = capability.Profiles[0];
    return new RollOutProfileImageCommand(
        CommandId: Guid.NewGuid(),
        CandidateId: Guid.NewGuid(),
        RecipeId: ImageRolloutTestData.DefaultRecipeId,
        ProfileId: profile.ProfileId,
        TargetDigest: TargetDigest,
        TargetPlatform: profile.Architecture,
        ExpectedCurrentImageReference: profile.CurrentImageReference,
        ExpectedCurrentImageDigest: profile.CurrentImageDigest,
        ExpectedCurrentLocalImageId: profile.CurrentLocalImageId,
        ExpectedCurrentWorkerRevision: profile.CurrentWorkerRevision,
        ExpectedStaticFingerprint: profile.StaticFingerprint,
        ExpectedPreservedConfigurationFingerprint:
            profile.PreservedConfigurationFingerprint,
        ExpectedRoutingFingerprint: profile.RoutingFingerprint,
        ExpectedDesiredGeneration: profile.DesiredGeneration,
        ExpectedDesiredStateHash: profile.DesiredStateHash,
        RequestedAt: ImageRolloutTestData.Now.AddSeconds(-30),
        ExpiresAt: ImageRolloutTestData.Now.AddMinutes(10));
  }

  private static ImageRolloutLedgerEntry CreateStartedLedgerEntry(
      RollOutProfileImageCommand command,
      ImageRolloutOperatorCapability capability)
  {
    var profile = capability.Profiles[0];
    return new ImageRolloutLedgerEntry(
        CommandId: command.CommandId,
        ProfileId: profile.ProfileId,
        CandidateId: command.CandidateId,
        RecipeId: command.RecipeId,
        TargetDigest: command.TargetDigest,
        TargetPlatform: command.TargetPlatform,
        RegistryRepository: ImageRolloutTestData.DefaultRegistryRepository,
        LocalManifestPath: Path.Combine(
            "unused",
            $"{command.CommandId:N}.json"),
        ExpectedCurrentImageReference: profile.CurrentImageReference,
        ExpectedCurrentImageDigest: profile.CurrentImageDigest,
        ExpectedCurrentLocalImageId: profile.CurrentLocalImageId,
        ExpectedCurrentWorkerRevision: profile.CurrentWorkerRevision,
        ExpectedStaticFingerprint: profile.StaticFingerprint,
        ExpectedPreservedConfigurationFingerprint:
            profile.PreservedConfigurationFingerprint,
        ExpectedRoutingFingerprint: profile.RoutingFingerprint,
        ExpectedDesiredGeneration: profile.DesiredGeneration,
        ExpectedDesiredStateHash: profile.DesiredStateHash,
        ResolvedPreOperationRevision: profile.CurrentWorkerRevision,
        ManifestSourcePath: null,
        StartedAt: ImageRolloutTestData.Now,
        Phase: ImageRolloutLedgerPhases.Started,
        Status: null,
        FailureCategory: null,
        Message: null,
        TargetWorkerRevision: null,
        ManagerConvergenceStatus: null,
        CurrentWorkers: null,
        StaleWorkers: null,
        LastError: null,
        CompletedAt: null);
  }

  // Finding 3: container mode never executes rollout.
  [Test]
  public async Task ExecuteAsync_Rejects_Command_In_Container_Mode(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(true),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("not-allowed");
      await Assert.That(process.InvocationCount).IsEqualTo(0);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 4: prove the Setup-Runner argument list is exactly the fixed
  // shape and never leaks -Image, -PullImage, -Labels, the registry
  // repository, the target digest, enrollment code, or credential values.
  [Test]
  public async Task ExecuteAsync_Setup_Runner_Arguments_Never_Leak_Command_Or_Credential_Values(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      options.EnrollmentCode = "hunter-sensitive-enrollment-code";
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, token) =>
        {
          await WriteConvergedStateAsync(root, token);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(process.LastRequest).IsNotNull();
      var args = process.LastRequest!.Arguments;
      // Fixed prefix + NamePrefix (schema-rejected property carried via
      // CLI) + local routing: -NoProfile, -File, Setup-Runner.ps1,
      // -Profile, <id>, -ProfilePath, <manifest>, -NamePrefix, <value>,
      // -Scope, repo, -AddRepos, <url>=<count>. 13 arguments and nothing
      // else in this healthy case.
      await Assert.That(args.Count).IsEqualTo(13);
      await Assert.That(args[0]).IsEqualTo("-NoProfile");
      await Assert.That(args[1]).IsEqualTo("-File");
      await Assert.That(args[2]).IsEqualTo("Setup-Runner.ps1");
      await Assert.That(args[3]).IsEqualTo("-Profile");
      await Assert.That(args[4]).IsEqualTo(ImageRolloutTestData.DefaultProfileId);
      await Assert.That(args[5]).IsEqualTo("-ProfilePath");
      // Argument 6 is the connector-generated manifest path; assert it is
      // scoped to that path shape rather than exposing any policy value.
      await Assert.That(args[6])
          .Contains($"{command.CommandId:N}.json");
      await Assert.That(args[7]).IsEqualTo("-NamePrefix");
      await Assert.That(args[8]).IsEqualTo("runner");
      await Assert.That(args[9]).IsEqualTo("-Scope");
      await Assert.That(args[10]).IsEqualTo("repo");
      await Assert.That(args[11]).IsEqualTo("-AddRepos");
      // The AddRepos value contains ONLY the local repository url and
      // count — never a candidate registry repository, target digest, or
      // any credential value.
      await Assert.That(args[12])
          .IsEqualTo($"{ImageRolloutTestData.DefaultRepositoryUrl}=" +
              $"{ImageRolloutTestData.DefaultRepositoryWorkers}");
      // Argument list must not contain any of the forbidden image/label/pull
      // flags; the manifest file carries those instead.
      await Assert.That(args.Contains("-Image")).IsFalse();
      await Assert.That(args.Contains("-PullImage")).IsFalse();
      await Assert.That(args.Contains("-Labels")).IsFalse();
      // Except for the fixed profile allowlist value on the -Profile flag,
      // no argument may contain the registry repository, target digest,
      // enrollment code, or any obvious credential token.
      foreach (var raw in args)
      {
        await Assert.That(raw.Contains(
            ImageRolloutTestData.DefaultRegistryRepository,
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(raw.Contains(
            TargetDigest,
            StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(raw.Contains(
            options.EnrollmentCode,
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(raw.Contains(
            "secret",
            StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(raw.Contains(
            "token",
            StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(raw.Contains(
            "password",
            StringComparison.OrdinalIgnoreCase)).IsFalse();
      }
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 2: a durable started ledger entry is always resolved even when
  // the operator subsequently disables ImageRolloutEnabled.
  [Test]
  public async Task ResolveInterruptedAsync_Terminalizes_When_Rollout_Was_Disabled_After_Start(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);
      var startedEntry = CreateStartedLedgerEntry(command, capability!);
      await ledger.RecordStartedAsync(startedEntry, cancellationToken);
      // Operator now disables rollout on the connector.
      options.ImageRolloutEnabled = false;
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          new FakeSetupProcessRunner
          {
            Handler = (_, _) => throw new InvalidOperationException(
                "process should not run for a terminalised interrupted entry"),
          },
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var outcomes = await executor.ResolveInterruptedAsync(cancellationToken);

      await Assert.That(outcomes).HasSingleItem();
      await Assert.That(outcomes[0].CommandId).IsEqualTo(command.CommandId);
      // Resolver returned null (disabled) → indeterminate is the correct
      // terminal classification; never permanently active/started.
      await Assert.That(outcomes[0].Status).IsEqualTo("indeterminate");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 2: a durable started ledger entry is resolved even when the
  // profile was subsequently removed from AllowedImageRolloutProfiles.
  [Test]
  public async Task ResolveInterruptedAsync_Terminalizes_When_Profile_Was_Removed_From_Allowlist(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);
      var startedEntry = CreateStartedLedgerEntry(command, capability!);
      await ledger.RecordStartedAsync(startedEntry, cancellationToken);
      // Operator now clears the allowlist for this profile.
      options.AllowedImageRolloutProfiles = Array.Empty<string>();
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          new FakeSetupProcessRunner
          {
            Handler = (_, _) => throw new InvalidOperationException(
                "process should not run for a terminalised interrupted entry"),
          },
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var outcomes = await executor.ResolveInterruptedAsync(cancellationToken);

      await Assert.That(outcomes).HasSingleItem();
      await Assert.That(outcomes[0].Status).IsEqualTo("indeterminate");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 2: a durable started ledger entry is resolved even when profile
  // state has become unavailable (locally missing or unreadable).
  [Test]
  public async Task ResolveInterruptedAsync_Terminalizes_When_Profile_State_Unavailable(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var ledger = ConnectorTestFactory.CreateImageRolloutLedger(options);
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);
      var startedEntry = CreateStartedLedgerEntry(command, capability!);
      await ledger.RecordStartedAsync(startedEntry, cancellationToken);
      // Wipe the profile state so the resolver returns null but the ledger
      // entry still exists.
      var profileDirectory = Path.Combine(
          root,
          ".pitcrew-state",
          ImageRolloutTestData.DefaultProfileId);
      Directory.Delete(profileDirectory, recursive: true);
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          new FakeSetupProcessRunner
          {
            Handler = (_, _) => throw new InvalidOperationException(
                "process should not run for a terminalised interrupted entry"),
          },
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));

      var outcomes = await executor.ResolveInterruptedAsync(cancellationToken);

      await Assert.That(outcomes).HasSingleItem();
      await Assert.That(outcomes[0].Status).IsEqualTo("indeterminate");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Finding 5: prune manifest history after a terminal transition so orphaned
  // reconstructions do not accumulate across long-running processes.
  [Test]
  public async Task ExecuteAsync_Prunes_Orphaned_Manifest_History_After_Terminal_Transition(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      // Keep the pruning cap tight to prove old entries fall off.
      options.ImageRolloutRetainedManifests = 2;
      var manifestBuilder =
          ConnectorTestFactory.CreateImageRolloutManifestBuilder(options);
      // Seed several unreferenced manifest files before running so we can
      // observe the pruning happening after the terminal transition.
      var staticProfileJson = await File.ReadAllTextAsync(
          Path.Combine(
              root,
              ".pitcrew-state",
              ImageRolloutTestData.DefaultProfileId,
              "static-profile.json"),
          cancellationToken);
      var stalePaths = new List<string>();
      for (var i = 0; i < 5; i++)
      {
        stalePaths.Add(manifestBuilder.BuildAndWriteManifest(
            Guid.NewGuid(),
            ImageRolloutTestData.DefaultProfileId,
            staticProfileJson,
            "ghcr.io/example/history",
            "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"));
        await Task.Delay(15, cancellationToken);
      }
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, token) =>
        {
          await WriteConvergedStateAsync(root, token);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("succeeded");
      var manifestDirectory = Path.Combine(
          options.ImageRolloutStatePath,
          "manifests");
      var remaining = Directory.GetFiles(manifestDirectory, "*.json");
      // The referenced (this rollout) manifest + the 2 most recent orphaned
      // manifests are retained; earlier orphans get swept.
      await Assert.That(remaining.Length).IsLessThanOrEqualTo(3);
      // The oldest orphaned manifest must be gone even though we did not
      // manually call PruneManifestHistory between runs.
      await Assert.That(File.Exists(stalePaths[0])).IsFalse();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Privacy safeguard: outcome LastError is a bounded closed literal after
  // a non-zero exit; never raw process output or exception text.
  [Test]
  public async Task ExecuteAsync_LastError_Is_Bounded_ExitCode_Literal_On_Non_Zero_Exit(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner
      {
        Handler = (_, _) => Task.FromResult(new SetupProcessResult(37, false)),
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("failed");
      await Assert.That(report.Outcome.LastError).IsEqualTo("exit-code:37");
      await Assert.That(report.Outcome.LastError!.Length)
          .IsLessThanOrEqualTo(
              ImageRolloutCommandExecutor.MaxLastErrorLength);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Privacy safeguard: outcome LastError is a bounded closed literal after
  // a timeout; the operator-facing Message stays generic.
  [Test]
  public async Task ExecuteAsync_LastError_Is_Bounded_Timeout_Literal_On_Timeout(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner
      {
        Handler = (_, _) => Task.FromResult(new SetupProcessResult(null, true)),
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.LastError)
          .IsEqualTo(ImageRolloutCommandExecutor.LastErrorTimedOut);
      await Assert.That(report.Outcome.LastError!.Length)
          .IsLessThanOrEqualTo(
              ImageRolloutCommandExecutor.MaxLastErrorLength);
      // The operator-facing message stays generic and never carries host
      // paths, exception text, or raw process output.
      await Assert.That(report.Outcome.Message).IsNotNull();
      await Assert.That(report.Outcome.Message).DoesNotContain(root);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Privacy safeguard: LastError uses the bounded postconditions literal
  // after a clean exit that did not apply the target digest.
  [Test]
  public async Task ExecuteAsync_LastError_Is_Bounded_Postconditions_Literal_When_Unchanged(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      // Handler exits cleanly but does NOT converge the local state, so
      // ClassifyPostState returns Unchanged and CompleteAsync uses the
      // postconditions-unverified bounded literal.
      var process = new FakeSetupProcessRunner
      {
        Handler = (_, _) => Task.FromResult(new SetupProcessResult(0, false)),
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("failed");
      await Assert.That(report.Outcome.LastError)
          .IsEqualTo(
              ImageRolloutCommandExecutor.LastErrorPostconditionsUnverified);
      await Assert.That(report.Outcome.LastError!.Length)
          .IsLessThanOrEqualTo(
              ImageRolloutCommandExecutor.MaxLastErrorLength);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // Every rejection outcome that the executor produces must land in the
  // closed protocol category set (not-allowed, stale-fence, expired,
  // unsupported, timed-out, unknown, or an operational failure) with
  // bounded LastError length. These assertions guarantee normalization
  // is applied everywhere and prevent regressions where a raw resolver
  // category (e.g. stale-observed-state, unsupported-schema) leaks into
  // the wire outcome.
  [Test]
  public async Task
      Rejected_Outcomes_From_Known_Branches_Use_Closed_Wire_Categories(
      CancellationToken cancellationToken)
  {
    var closedRejectionCategories = new[]
    {
      "not-allowed",
      "recipe-not-allowed",
      "registry-not-allowed",
      "stale-fence",
      "expired",
      "unsupported",
      "unsupported-architecture",
      "unsupported-topology",
      "operation-active",
      "timed-out",
      "unknown",
    };

    // Branch 1: rollout disabled → not-allowed (already validated), but
    // also assert the wire-safe closed category set.
    await AssertRejectionCategoryIsClosedAsync(
        cancellationToken,
        configureOptions: options => options.ImageRolloutEnabled = false,
        expectedCategory: "not-allowed",
        allowedCategories: closedRejectionCategories);

    // Branch 2: unallowlisted recipe → distinct recipe-not-allowed
    // category (not the generic not-allowed) so operators see the
    // exact recipe-scoped rejection.
    await AssertRejectionCategoryIsClosedAsync(
        cancellationToken,
        configureOptions: options =>
            options.ImageRolloutRecipes.Clear(),
        expectedCategory: "recipe-not-allowed",
        allowedCategories: closedRejectionCategories);

    // Branch 3: expired command → expired.
    await AssertRejectionCategoryIsClosedAsync(
        cancellationToken,
        configureOptions: _ => { },
        expectedCategory: "expired",
        allowedCategories: closedRejectionCategories,
        mutateCommand: c => c with
        {
          ExpiresAt = ImageRolloutTestData.Now
              .AddMinutes(-1),
        });
  }

  private static async Task AssertRejectionCategoryIsClosedAsync(
      CancellationToken cancellationToken,
      Action<ConnectorOptions> configureOptions,
      string expectedCategory,
      IReadOnlyList<string> allowedCategories,
      Func<RollOutProfileImageCommand, RollOutProfileImageCommand>?
          mutateCommand = null)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      configureOptions(options);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);
      if (mutateCommand is not null)
      {
        command = mutateCommand(command);
      }

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory)
          .IsEqualTo(expectedCategory);
      // Regardless of which rejection branch fired, the wire category
      // must be in the closed set the protocol validator accepts.
      await Assert.That(
              allowedCategories.Contains(report.Outcome.FailureCategory!))
          .IsTrue()
          .Because(
              "Every wire-produced rejection category must be in the " +
              "closed protocol set to survive dashboard validation.");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // ClassifyPostState must NOT report succeeded when routing drifts
  // between the pre-operation and post-operation reads even if digest
  // and preserved fingerprints match the target. Routing drift means
  // the local environment changed underneath the rollout and the
  // outcome is indeterminate, not clean success.
  [Test]
  public async Task
      ExecuteAsync_Routing_Drift_Prevents_Success_Classification(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, ct) =>
        {
          // Simulate a "successful" rollout at the digest+preserved
          // level, but also drift routing so ClassifyPostState must not
          // report succeeded.
          await WriteConvergedStateAsync(root, ct);
          await ImageRolloutTestData.WriteDesiredWithMalformedRepositoriesAsync(
              root,
              ct);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      // Per ADR-0013 ClassifyPostState contract, drifted routing means
      // the digest/preserved match alone must NEVER classify as
      // succeeded, and must NEVER classify as unchanged (which would
      // map to the "failed"/process-failure outcome). With post-state
      // routing unreadable, the executor honestly falls through to the
      // "indeterminate" branch — postconditions cannot be proved.
      await Assert.That(report.Outcome.Status).IsNotEqualTo("succeeded");
      await Assert.That(report.Outcome.Status).IsNotEqualTo("failed");
      await Assert.That(report.Outcome.Status).IsEqualTo("indeterminate");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  // ClassifyPostState must NOT report succeeded when desired generation
  // advances underneath the rollout even if digest/preserved match.
  [Test]
  public async Task
      ExecuteAsync_Generation_Drift_Prevents_Success_Classification(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, ct) =>
        {
          await WriteConvergedStateAsync(root, ct);
          // Advance the desired generation so post-state routing/hash
          // fences appear to have moved even though digest/preserved
          // match. Success must fail closed.
          var desiredPath = Path.Combine(
              root,
              ".pitcrew-state",
              ImageRolloutTestData.DefaultProfileId,
              "desired-capacity.json");
          var desiredText = await File.ReadAllTextAsync(desiredPath, ct);
          desiredText = desiredText.Replace(
              "\"generation\":7",
              "\"generation\":999",
              StringComparison.Ordinal);
          await File.WriteAllTextAsync(desiredPath, desiredText, ct);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      // Generation drift means the desired generation fence no longer
      // matches. Success must never be declared and Unchanged (which
      // requires generation unchanged) must never be declared either.
      await Assert.That(report.Outcome.Status).IsNotEqualTo("succeeded");
      await Assert.That(report.Outcome.Status).IsNotEqualTo("failed");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task
      ExecuteAsync_Missing_Post_Worker_Counts_Prevents_Success_Classification(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await ImageRolloutTestData.WriteHealthyStateAsync(
          root,
          cancellationToken);
      var options = ImageRolloutTestData.CreateOperatorOptions(root);
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, ct) =>
        {
          // Simulate a successful setup: digest is applied, revision
          // rolled, every fence still matches. Then overwrite observed
          // state so the update target identity remains consistent with
          // the applied digest/revision, but currentWorkers and
          // staleWorkers are omitted. The resolver treats this as
          // "stale-observed-state" and returns null counts on the
          // profile state.
          await WriteConvergedStateAsync(root, ct);
          await ImageRolloutTestData.WriteObservedWorkersAsync(
              root,
              currentWorkers: null,
              staleWorkers: null,
              ct,
              observedAt: ImageRolloutTestData.Now,
              status: "current",
              targetImage:
                  $"{ImageRolloutTestData.DefaultRegistryRepository}"
                  + $"@{TargetDigest}",
              targetRevision: TargetWorkerRevision);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateImageRolloutExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(ImageRolloutTestData.Now));
      var capability = await ConnectorTestFactory
          .CreateImageRolloutResolver(
              ImageRolloutTestData.CreateOperatorOptions(root),
              new FakeHostExecutionEnvironment(false),
              new FixedTimeProvider(ImageRolloutTestData.Now))
          .ReadCapabilityAsync(cancellationToken);
      var command = CreateFencedCommand(capability!);

      var report = await executor.ExecuteAsync(command, cancellationToken);

      // Even when digest+fences appear applied, missing observed worker
      // counts leave convergence unproved. Success/failed classification
      // is not truthful; the outcome must be indeterminate/unknown.
      await Assert.That(report.Outcome.Status)
          .IsNotEqualTo("succeeded")
          .Because(
              "issue #151 requires observed CurrentWorkers/StaleWorkers "
              + "before a rollout can be classified succeeded");
      await Assert.That(report.Outcome.Status).IsEqualTo("indeterminate");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("unknown");
      // The indeterminate branch does not require observed worker counts
      // (the protocol contract only requires them for succeeded); the
      // outcome truthfully reports null counts rather than fabricating
      // a converged value the connector never observed.
      await Assert.That(report.Outcome.CurrentWorkers).IsNull();
      await Assert.That(report.Outcome.StaleWorkers).IsNull();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  private static async Task WriteConvergedStateAsync(
      string root,
      CancellationToken cancellationToken)
  {
    // Simulate a successful rollout by rewriting the static profile so its
    // configuration.image is now the digest-qualified target reference (the
    // resolver reads current digest only from a digest-qualified image),
    // and the worker revision matches the new target. Observed state is
    // updated to acknowledge the same target with matching current workers.
    await ImageRolloutTestData.WriteHealthyStateAsync(
        root,
        cancellationToken,
        currentImage:
            $"{ImageRolloutTestData.DefaultRegistryRepository}@{TargetDigest}");
    var profileDirectory = Path.Combine(
        root,
        ".pitcrew-state",
        ImageRolloutTestData.DefaultProfileId);
    var staticPath = Path.Combine(profileDirectory, "static-profile.json");
    var text = await File.ReadAllTextAsync(staticPath, cancellationToken);
    text = text.Replace(
        ImageRolloutTestData.CurrentWorkerRevision,
        TargetWorkerRevision,
        StringComparison.Ordinal);
    text = text.Replace(
        $"\"fingerprint\":\"{ImageRolloutTestData.StaticFingerprint}\"",
        $"\"fingerprint\":\"{TargetStaticFingerprint}\"",
        StringComparison.Ordinal);
    await File.WriteAllTextAsync(staticPath, text, cancellationToken);
    // Observed state must ack the same digest-qualified image and new
    // revision as the current update target so IsUpdateConsistentWithCurrent
    // stays true.
    await ImageRolloutTestData.WriteObservedWorkersAsync(
        root,
        currentWorkers: ImageRolloutTestData.DefaultRepositoryWorkers,
        staleWorkers: 0,
        cancellationToken,
        observedAt: ImageRolloutTestData.Now,
        status: "current",
        targetImage:
            $"{ImageRolloutTestData.DefaultRegistryRepository}@{TargetDigest}",
        targetRevision: TargetWorkerRevision);
  }
}
