using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class RecoveryCommandExecutorTests
{
  [Test]
  public async Task ExecuteAsync_Invokes_Recovery_Once_And_Reports_Success(
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
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, token) =>
        {
          await RecoveryTestData.WriteHealthyObservedStateAsync(
              root,
              "manager-2",
              token);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));
      var command = RecoveryTestData.CreateFencedCommand();

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(1);
      await Assert.That(report.Progress).IsNotNull();
      await Assert.That(report.Progress!.Phase).IsEqualTo("started");
      await Assert.That(report.Outcome.Status).IsEqualTo("succeeded");
      await Assert.That(report.Outcome.FailureCategory).IsNull();
      await Assert.That(report.Outcome.BeforeManagerInstanceId)
          .IsEqualTo("manager-1");
      await Assert.That(report.Outcome.AfterManagerInstanceId)
          .IsEqualTo("manager-2");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Builds_Locally_Reconstructed_Recovery_Arguments(
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
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, token) =>
        {
          await RecoveryTestData.WriteHealthyObservedStateAsync(
              root,
              "manager-2",
              token);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      await executor.ExecuteAsync(
          RecoveryTestData.CreateFencedCommand(),
          cancellationToken);

      await Assert.That(process.LastRequest).IsNotNull();
      await Assert.That(process.LastRequest!.Executable).IsEqualTo("pwsh");
      await Assert.That(process.LastRequest.WorkingDirectory)
          .IsEqualTo(Path.GetFullPath(root));
      await Assert.That(process.LastRequest.Arguments).IsEquivalentTo(
          new[]
          {
            "-NoProfile",
            "-File",
            "Setup-Runner.ps1",
            "-Profile",
            "default",
            "-RecoverManager",
            "-ExpectedManagerInstanceId",
            "manager-1",
            "-ExpectedGeneration",
            "7",
            "-ExpectedDesiredStateHash",
            RecoveryTestData.DesiredStateHash,
            "-RecoveryTimeoutSeconds",
            options.RecoveryCommandTimeoutSeconds.ToString(),
          });
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_When_Recovery_Is_Disabled(
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
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var report = await executor.ExecuteAsync(
          RecoveryTestData.CreateFencedCommand(),
          cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(0);
      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("not-allowed");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_In_Container_Mode(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-1",
          cancellationToken);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          RecoveryTestData.CreateRecoveryOptions(root),
          process,
          new FakeHostExecutionEnvironment(true),
          new FixedTimeProvider(RecoveryTestData.Now));

      var report = await executor.ExecuteAsync(
          RecoveryTestData.CreateFencedCommand(),
          cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(0);
      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("not-allowed");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_Stale_Fences(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-2",
          cancellationToken);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          RecoveryTestData.CreateRecoveryOptions(root),
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var report = await executor.ExecuteAsync(
          RecoveryTestData.CreateFencedCommand(),
          cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(0);
      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("stale-fence");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_Expired_Command(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-1",
          cancellationToken);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          RecoveryTestData.CreateRecoveryOptions(root),
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now.AddMinutes(10)));

      var report = await executor.ExecuteAsync(
          RecoveryTestData.CreateFencedCommand(),
          cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(0);
      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("expired");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_Manager_Shutdown_Request(
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
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          RecoveryTestData.CreateRecoveryOptions(root),
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var report = await executor.ExecuteAsync(
          RecoveryTestData.CreateFencedCommand(),
          cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(0);
      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory)
          .IsEqualTo("manager-unresolved");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Rejects_While_A_Capacity_Operation_Runs(
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
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          options,
          gate,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));
      using var lease = gate.AcquireOrNull("default");

      var report = await executor.ExecuteAsync(
          RecoveryTestData.CreateFencedCommand(),
          cancellationToken);

      await Assert.That(lease).IsNotNull();
      await Assert.That(process.InvocationCount).IsEqualTo(0);
      await Assert.That(report.Outcome.Status).IsEqualTo("rejected");
      await Assert.That(report.Outcome.FailureCategory)
          .IsEqualTo("operation-active");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Blocks_Capacity_While_Recovery_Runs(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await CapacityTestData.WriteSingleRepositoryProfileAsync(
          root,
          7,
          24,
          cancellationToken);
      var options = RecoveryTestData.CreateRecoveryOptions(root);
      var gate = ConnectorTestFactory.CreateOperationGate(options);
      var capacityProcess = new FakeSetupProcessRunner();
      var capacityExecutor = ConnectorTestFactory.CreateCapacityExecutor(
          options,
          ConnectorTestFactory.CreateCapacityResolver(options),
          gate,
          capacityProcess,
          new FixedTimeProvider(RecoveryTestData.Now));
      using var lease = gate.AcquireOrNull("default");

      var outcome = await capacityExecutor.ExecuteAsync(
          new SetCapacityCommand(
              Guid.NewGuid(),
              "default",
              7,
              40,
              RecoveryTestData.Now.AddMinutes(5)),
          cancellationToken);

      await Assert.That(lease).IsNotNull();
      await Assert.That(capacityProcess.InvocationCount).IsEqualTo(0);
      await Assert.That(outcome.Status).IsEqualTo("rejected");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Never_Executes_A_Redelivered_Command_Again(
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
      var process = new FakeSetupProcessRunner
      {
        Handler = async (_, token) =>
        {
          await RecoveryTestData.WriteHealthyObservedStateAsync(
              root,
              "manager-2",
              token);
          return new SetupProcessResult(0, false);
        },
      };
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));
      var command = RecoveryTestData.CreateFencedCommand();

      var first = await executor.ExecuteAsync(command, cancellationToken);
      var second = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(1);
      await Assert.That(first.Outcome.Status).IsEqualTo("succeeded");
      await Assert.That(second.Progress).IsNull();
      await Assert.That(second.Outcome.Status).IsEqualTo("succeeded");
      await Assert.That(second.Outcome.AfterManagerInstanceId)
          .IsEqualTo("manager-2");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Reports_Process_Failure_Without_Retry(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-1",
          cancellationToken);
      var process = new FakeSetupProcessRunner
      {
        Handler = static (_, _) => Task.FromResult(
            new SetupProcessResult(3, false)),
      };
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          RecoveryTestData.CreateRecoveryOptions(root),
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var report = await executor.ExecuteAsync(
          RecoveryTestData.CreateFencedCommand(),
          cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(1);
      await Assert.That(report.Outcome.Status).IsEqualTo("failed");
      await Assert.That(report.Outcome.FailureCategory)
          .IsEqualTo("process-failure");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Reports_Indeterminate_On_Unproven_Timeout(
      CancellationToken cancellationToken)
  {
    var root = CapacityTestData.CreateTemporaryDirectory();
    try
    {
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-1",
          cancellationToken);
      var process = new FakeSetupProcessRunner
      {
        Handler = static (_, _) => Task.FromResult(
            new SetupProcessResult(null, true)),
      };
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          RecoveryTestData.CreateRecoveryOptions(root),
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var report = await executor.ExecuteAsync(
          RecoveryTestData.CreateFencedCommand(),
          cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(1);
      await Assert.That(report.Outcome.Status).IsEqualTo("indeterminate");
      await Assert.That(report.Outcome.FailureCategory).IsEqualTo("timeout");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ResolveInterruptedAsync_Proves_Recovery_After_A_Crash(
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
      var command = RecoveryTestData.CreateFencedCommand();
      await WriteStartedLedgerEntryAsync(options, command, cancellationToken);
      await RecoveryTestData.WriteHealthyObservedStateAsync(
          root,
          "manager-2",
          cancellationToken);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now.AddMinutes(1)));

      var outcomes = await executor.ResolveInterruptedAsync(cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(0);
      await Assert.That(outcomes).HasSingleItem();
      await Assert.That(outcomes[0].CommandId).IsEqualTo(command.CommandId);
      await Assert.That(outcomes[0].Status).IsEqualTo("succeeded");
      await Assert.That(outcomes[0].AfterManagerInstanceId)
          .IsEqualTo("manager-2");
      await Assert.That(
              await executor.ResolveInterruptedAsync(cancellationToken))
          .IsEmpty();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ResolveInterruptedAsync_Reports_Indeterminate_Without_Proof(
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
      var command = RecoveryTestData.CreateFencedCommand();
      await WriteStartedLedgerEntryAsync(options, command, cancellationToken);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now.AddMinutes(1)));

      var outcomes = await executor.ResolveInterruptedAsync(cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(0);
      await Assert.That(outcomes).HasSingleItem();
      await Assert.That(outcomes[0].Status).IsEqualTo("indeterminate");
      await Assert.That(outcomes[0].AfterManagerInstanceId).IsNull();
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [Test]
  public async Task ExecuteAsync_Resolves_A_Redelivered_Interrupted_Command(
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
      var command = RecoveryTestData.CreateFencedCommand();
      await WriteStartedLedgerEntryAsync(options, command, cancellationToken);
      var process = new FakeSetupProcessRunner();
      var executor = ConnectorTestFactory.CreateRecoveryExecutor(
          options,
          process,
          new FakeHostExecutionEnvironment(false),
          new FixedTimeProvider(RecoveryTestData.Now));

      var report = await executor.ExecuteAsync(command, cancellationToken);

      await Assert.That(process.InvocationCount).IsEqualTo(0);
      await Assert.That(report.Progress).IsNull();
      await Assert.That(report.Outcome.Status).IsEqualTo("indeterminate");
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  private static async Task WriteStartedLedgerEntryAsync(
      ConnectorOptions options,
      RecoverManagerCommand command,
      CancellationToken cancellationToken)
  {
    var ledger = ConnectorTestFactory.CreateLedger(options);
    await ledger.RecordStartedAsync(
        new RecoveryLedgerEntry(
            command.CommandId,
            command.ProfileId,
            command.ExpectedManagerInstanceId,
            command.ExpectedGeneration,
            command.ExpectedDesiredStateHash,
            command.ExpectedManagerInstanceId,
            command.ExpectedGeneration,
            command.ExpectedDesiredStateHash,
            RecoveryTestData.Now,
            RecoveryLedgerPhases.Started,
            null,
            null,
            null,
            null,
            null),
        cancellationToken);
  }
}
