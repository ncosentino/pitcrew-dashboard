using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace PitCrew.Connector.Features.Sync.Tests;

internal static class ConnectorTestFactory
{
  public static LocalProfileStateLocator CreateStateLocator(
      ConnectorOptions options) =>
      new(
          Options.Create(options),
          NullLogger<LocalProfileStateLocator>.Instance);

  public static LocalProfileOperationGate CreateOperationGate(
      ConnectorOptions options) =>
      new(
          Options.Create(options),
          NullLogger<LocalProfileOperationGate>.Instance);

  public static CapacityProfileResolver CreateCapacityResolver(
      ConnectorOptions options) =>
      new(
          CreateStateLocator(options),
          Options.Create(options),
          NullLogger<CapacityProfileResolver>.Instance);

  public static CapacityCommandExecutor CreateCapacityExecutor(
      ConnectorOptions options,
      CapacityProfileResolver resolver,
      ISetupProcessRunner processRunner,
      TimeProvider timeProvider) =>
      CreateCapacityExecutor(
          options,
          resolver,
          CreateOperationGate(options),
          processRunner,
          timeProvider);

  public static CapacityCommandExecutor CreateCapacityExecutor(
      ConnectorOptions options,
      CapacityProfileResolver resolver,
      LocalProfileOperationGate operationGate,
      ISetupProcessRunner processRunner,
      TimeProvider timeProvider) =>
      new(
          resolver,
          operationGate,
          processRunner,
          Options.Create(options),
          timeProvider,
          NullLogger<CapacityCommandExecutor>.Instance);

  public static RecoveryProfileResolver CreateRecoveryResolver(
      ConnectorOptions options,
      IHostExecutionEnvironment executionEnvironment,
      TimeProvider timeProvider) =>
      CreateRecoveryResolver(
          options,
          CreateOperationGate(options),
          executionEnvironment,
          timeProvider);

  public static RecoveryProfileResolver CreateRecoveryResolver(
      ConnectorOptions options,
      LocalProfileOperationGate operationGate,
      IHostExecutionEnvironment executionEnvironment,
      TimeProvider timeProvider) =>
      new(
          CreateStateLocator(options),
          operationGate,
          executionEnvironment,
          Options.Create(options),
          timeProvider,
          NullLogger<RecoveryProfileResolver>.Instance);

  public static RecoveryCommandLedger CreateLedger(
      ConnectorOptions options) =>
      new(
          Options.Create(options),
          NullLogger<RecoveryCommandLedger>.Instance);

  public static RecoveryCommandExecutor CreateRecoveryExecutor(
      ConnectorOptions options,
      ISetupProcessRunner processRunner,
      IHostExecutionEnvironment executionEnvironment,
      TimeProvider timeProvider) =>
      CreateRecoveryExecutor(
          options,
          CreateOperationGate(options),
          processRunner,
          executionEnvironment,
          timeProvider);

  public static RecoveryCommandExecutor CreateRecoveryExecutor(
      ConnectorOptions options,
      LocalProfileOperationGate operationGate,
      ISetupProcessRunner processRunner,
      IHostExecutionEnvironment executionEnvironment,
      TimeProvider timeProvider) =>
      new(
          CreateRecoveryResolver(
              options,
              operationGate,
              executionEnvironment,
              timeProvider),
          CreateLedger(options),
          operationGate,
          processRunner,
          executionEnvironment,
          Options.Create(options),
          timeProvider,
          NullLogger<RecoveryCommandExecutor>.Instance);
}
