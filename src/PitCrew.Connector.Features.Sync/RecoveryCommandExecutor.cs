using System.ComponentModel;
using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Executes typed manager-recovery commands at most once through PitCrew's
/// first-class local recovery operation.
/// </summary>
[DoNotAutoRegister]
internal sealed partial class RecoveryCommandExecutor(
    RecoveryProfileResolver _profileResolver,
    RecoveryCommandLedger _ledger,
    LocalProfileOperationGate _operationGate,
    ISetupProcessRunner _processRunner,
    IHostExecutionEnvironment _executionEnvironment,
    IOptions<ConnectorOptions> _options,
    TimeProvider _timeProvider,
    ILogger<RecoveryCommandExecutor> _logger)
{
  /// <summary>
  /// Reads the recovery capability advertised to the dashboard.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels the read.</param>
  /// <returns>The capability, or <see langword="null"/> when recovery is locally disabled.</returns>
  public Task<RecoveryOperatorCapability?> ReadCapabilityAsync(
      CancellationToken cancellationToken) =>
      _profileResolver.ReadCapabilityAsync(cancellationToken);

  /// <summary>
  /// Resolves every attempt that started but never reached a terminal state.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels resolution.</param>
  /// <returns>Terminal outcomes proved or classified as indeterminate.</returns>
  public async Task<IReadOnlyList<RecoveryCommandOutcome>> ResolveInterruptedAsync(
      CancellationToken cancellationToken)
  {
    if (!IsLocallyEnabled())
    {
      return [];
    }

    var outcomes = new List<RecoveryCommandOutcome>();
    IReadOnlyList<RecoveryLedgerEntry> unresolved;
    try
    {
      unresolved = await _ledger.ReadUnresolvedAsync(cancellationToken);
    }
    catch (IOException exception)
    {
      LogLedgerFailure(exception.Message);
      return [];
    }
    catch (UnauthorizedAccessException exception)
    {
      LogLedgerFailure(exception.Message);
      return [];
    }

    foreach (var entry in unresolved)
    {
      var resolved = await ResolveInterruptedEntryAsync(
          entry,
          cancellationToken);
      if (resolved is not null)
      {
        outcomes.Add(resolved);
      }
    }
    return outcomes;
  }

  /// <summary>
  /// Executes one typed recovery command at most once.
  /// </summary>
  /// <param name="command">Typed command and expected fences.</param>
  /// <param name="cancellationToken">Token that cancels execution.</param>
  /// <returns>The durable progress and terminal outcome to report.</returns>
  public async Task<RecoveryExecutionReport> ExecuteAsync(
      RecoverManagerCommand command,
      CancellationToken cancellationToken)
  {
    if (!IsLocallyEnabled())
    {
      return Rejected(
          command,
          "not-allowed",
          "Manager recovery is disabled on this connector.",
          null);
    }

    RecoveryLedgerEntry? recorded;
    try
    {
      recorded = await _ledger.FindAsync(
          command.CommandId,
          cancellationToken);
    }
    catch (IOException exception)
    {
      LogLedgerFailure(exception.Message);
      return Rejected(
          command,
          "unknown",
          "The local recovery ledger could not be read.",
          null);
    }
    catch (UnauthorizedAccessException exception)
    {
      LogLedgerFailure(exception.Message);
      return Rejected(
          command,
          "unknown",
          "The local recovery ledger could not be read.",
          null);
    }
    if (recorded is not null)
    {
      return await ReportRecordedAsync(recorded, cancellationToken);
    }

    var completedAt = _timeProvider.GetUtcNow();
    if (command.ExpiresAt <= completedAt)
    {
      return Rejected(
          command,
          "expired",
          "Recovery command expired before execution.",
          null);
    }
    if (command.ExpiresAt - command.RequestedAt >
        TimeSpan.FromSeconds(
            _options.Value.RecoveryCommandMaximumExpirySeconds))
    {
      return Rejected(
          command,
          "not-allowed",
          "Recovery command lifetime exceeds local policy.",
          null);
    }

    using var lease = _operationGate.AcquireOrNull(command.ProfileId);
    if (lease is null)
    {
      return Rejected(
          command,
          "operation-active",
          "Another local profile operation is already active.",
          null);
    }

    var resolution = await _profileResolver.ResolveAsync(
        command.ProfileId,
        cancellationToken);
    if (resolution.Profile is null)
    {
      return Rejected(
          command,
          resolution.FailureCategory ?? "unknown",
          resolution.Error ?? "Profile is unavailable for recovery.",
          null);
    }

    var profile = resolution.Profile;
    if (!profile.RecoveryAllowed)
    {
      return Rejected(
          command,
          "not-allowed",
          "Local policy no longer allows recovery for the profile.",
          profile.ManagerInstanceId);
    }
    if (!profile.ManagerContractSupported)
    {
      return Rejected(
          command,
          "not-allowed",
          "The local manager contract does not support recovery.",
          profile.ManagerInstanceId);
    }
    if (!profile.SingleManagerResolved)
    {
      return Rejected(
          command,
          "manager-unresolved",
          "Exactly one running manager could not be resolved locally.",
          profile.ManagerInstanceId);
    }
    if (profile.ObservedStateAgeSeconds >
        _options.Value.RecoveryObservedStateMaximumAgeSeconds)
    {
      return Rejected(
          command,
          "stale-fence",
          "Observed manager state is older than local policy allows.",
          profile.ManagerInstanceId);
    }
    if (!string.Equals(
            profile.ManagerInstanceId,
            command.ExpectedManagerInstanceId,
            StringComparison.Ordinal) ||
        profile.Generation != command.ExpectedGeneration ||
        !string.Equals(
            profile.DesiredStateHash,
            command.ExpectedDesiredStateHash,
            StringComparison.OrdinalIgnoreCase))
    {
      return Rejected(
          command,
          "stale-fence",
          "Local manager state changed before execution.",
          profile.ManagerInstanceId);
    }

    var startedAt = _timeProvider.GetUtcNow();
    var entry = new RecoveryLedgerEntry(
        command.CommandId,
        profile.ProfileId,
        command.ExpectedManagerInstanceId,
        command.ExpectedGeneration,
        command.ExpectedDesiredStateHash,
        profile.ManagerInstanceId,
        profile.Generation,
        profile.DesiredStateHash,
        startedAt,
        RecoveryLedgerPhases.Started,
        null,
        null,
        null,
        null,
        null);
    bool claimed;
    try
    {
      claimed = await _ledger.RecordStartedAsync(
          entry,
          cancellationToken);
    }
    catch (IOException exception)
    {
      LogLedgerFailure(exception.Message);
      return Rejected(
          command,
          "unknown",
          "The local recovery ledger could not be written.",
          profile.ManagerInstanceId);
    }
    catch (UnauthorizedAccessException exception)
    {
      LogLedgerFailure(exception.Message);
      return Rejected(
          command,
          "unknown",
          "The local recovery ledger could not be written.",
          profile.ManagerInstanceId);
    }
    if (!claimed)
    {
      var duplicate = await _ledger.FindAsync(
          command.CommandId,
          cancellationToken);
      return duplicate is null
          ? Rejected(
              command,
              "unknown",
              "The local recovery ledger could not be read.",
              profile.ManagerInstanceId)
          : await ReportRecordedAsync(duplicate, cancellationToken);
    }

    var progress = new RecoveryCommandProgress(
        command.CommandId,
        "started",
        startedAt);
    LogExecutionStarted(command.CommandId, profile.ProfileId);

    SetupProcessResult result;
    try
    {
      result = await _processRunner.RunAsync(
          new SetupProcessRequest(
              _options.Value.PowerShellExecutable,
              Path.GetFullPath(_options.Value.PitCrewRoot),
              BuildArguments(
                  profile,
                  _options.Value.RecoveryCommandTimeoutSeconds),
              TimeSpan.FromSeconds(
                  _options.Value.RecoveryCommandTimeoutSeconds + 60)),
          cancellationToken);
    }
    catch (Win32Exception exception)
    {
      return await CompleteAsync(
          entry,
          progress,
          "failed",
          "process-failure",
          "The local PowerShell process could not be started.",
          null,
          exception.Message,
          cancellationToken);
    }
    catch (IOException exception)
    {
      return await CompleteAsync(
          entry,
          progress,
          "failed",
          "process-failure",
          "The local PitCrew recovery operation could not be executed.",
          null,
          exception.Message,
          cancellationToken);
    }

    var after = await _profileResolver.ResolveAsync(
        command.ProfileId,
        cancellationToken);
    var recovered = IsRecovered(entry, after.Profile);
    if (result.TimedOut)
    {
      return recovered
          ? await CompleteAsync(
              entry,
              progress,
              "succeeded",
              null,
              "PitCrew replaced the fenced manager before the local timeout elapsed.",
              after.Profile?.ManagerInstanceId,
              "timed out",
              cancellationToken)
          : await CompleteAsync(
              entry,
              progress,
              "indeterminate",
              "timeout",
              "PitCrew recovery exceeded the local timeout without proven evidence.",
              null,
              "timed out",
              cancellationToken);
    }
    if (result.ExitCode != 0)
    {
      return await CompleteAsync(
          entry,
          progress,
          "failed",
          "process-failure",
          "PitCrew did not report a verified recovery.",
          after.Profile?.ManagerInstanceId,
          $"exited with code {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
          cancellationToken);
    }
    return recovered
        ? await CompleteAsync(
            entry,
            progress,
            "succeeded",
            null,
            "PitCrew replaced the fenced manager.",
            after.Profile?.ManagerInstanceId,
            null,
            cancellationToken)
        : await CompleteAsync(
            entry,
            progress,
            "indeterminate",
            "unknown",
            "PitCrew reported success without locally provable postconditions.",
            null,
            "postconditions were not provable",
            cancellationToken);
  }

  internal static IReadOnlyList<string> BuildArguments(
      RecoveryProfileState profile,
      int timeoutSeconds)
  {
    var arguments = new List<string>
    {
      "-NoProfile",
      "-File",
      Path.Combine("Setup-Runner.ps1"),
      "-Profile",
      profile.ProfileId,
      "-RecoverManager",
      "-ExpectedManagerInstanceId",
      profile.ManagerInstanceId,
      "-ExpectedGeneration",
      profile.Generation.ToString(CultureInfo.InvariantCulture),
    };
    if (!string.IsNullOrWhiteSpace(profile.DesiredStateHash))
    {
      arguments.Add("-ExpectedDesiredStateHash");
      arguments.Add(profile.DesiredStateHash);
    }
    arguments.Add("-RecoveryTimeoutSeconds");
    arguments.Add(timeoutSeconds.ToString(CultureInfo.InvariantCulture));
    return arguments;
  }

  private async Task<RecoveryCommandOutcome?> ResolveInterruptedEntryAsync(
      RecoveryLedgerEntry entry,
      CancellationToken cancellationToken)
  {
    var after = await _profileResolver.ResolveAsync(
        entry.ProfileId,
        cancellationToken);
    var recovered = IsRecovered(entry, after.Profile);
    var completedAt = _timeProvider.GetUtcNow();
    var resolved = entry with
    {
      Phase = RecoveryLedgerPhases.Terminal,
      Status = recovered
          ? "succeeded"
          : "indeterminate",
      FailureCategory = recovered
          ? null
          : "unknown",
      Message = recovered
          ? "An interrupted recovery is proved by a replaced healthy manager."
          : "An interrupted recovery could not be proved and was not repeated.",
      AfterManagerInstanceId = recovered
          ? after.Profile?.ManagerInstanceId
          : null,
      CompletedAt = completedAt,
    };
    try
    {
      await _ledger.RecordTerminalAsync(resolved, cancellationToken);
    }
    catch (IOException exception)
    {
      LogLedgerFailure(exception.Message);
      return null;
    }
    catch (UnauthorizedAccessException exception)
    {
      LogLedgerFailure(exception.Message);
      return null;
    }
    LogInterruptedResolved(entry.CommandId, resolved.Status!);
    return ToOutcome(resolved);
  }

  private async Task<RecoveryExecutionReport> ReportRecordedAsync(
      RecoveryLedgerEntry recorded,
      CancellationToken cancellationToken)
  {
    LogDuplicateSuppressed(recorded.CommandId);
    if (string.Equals(
        recorded.Phase,
        RecoveryLedgerPhases.Terminal,
        StringComparison.Ordinal))
    {
      return new RecoveryExecutionReport(null, ToOutcome(recorded));
    }

    var resolved = await ResolveInterruptedEntryAsync(
        recorded,
        cancellationToken);
    return new RecoveryExecutionReport(
        null,
        resolved ?? new RecoveryCommandOutcome(
            recorded.CommandId,
            "indeterminate",
            "unknown",
            "A previously started recovery could not be resolved locally.",
            recorded.ResolvedManagerInstanceId,
            null,
            _timeProvider.GetUtcNow()));
  }

  private async Task<RecoveryExecutionReport> CompleteAsync(
      RecoveryLedgerEntry entry,
      RecoveryCommandProgress progress,
      string status,
      string? failureCategory,
      string message,
      string? afterManagerInstanceId,
      string? failureReason,
      CancellationToken cancellationToken)
  {
    var completed = entry with
    {
      Phase = RecoveryLedgerPhases.Terminal,
      Status = status,
      FailureCategory = failureCategory,
      Message = message,
      AfterManagerInstanceId = afterManagerInstanceId,
      CompletedAt = _timeProvider.GetUtcNow(),
    };
    try
    {
      await _ledger.RecordTerminalAsync(completed, cancellationToken);
    }
    catch (IOException exception)
    {
      LogLedgerFailure(exception.Message);
    }
    catch (UnauthorizedAccessException exception)
    {
      LogLedgerFailure(exception.Message);
    }
    if (failureReason is null)
    {
      LogExecutionSucceeded(entry.CommandId, entry.ProfileId);
    }
    else
    {
      LogExecutionFailed(entry.CommandId, status, failureReason);
    }
    return new RecoveryExecutionReport(
        progress,
        ToOutcome(completed));
  }

  private static bool IsRecovered(
      RecoveryLedgerEntry entry,
      RecoveryProfileState? after) =>
      after is not null &&
      after.SingleManagerResolved &&
      !string.IsNullOrWhiteSpace(after.ManagerInstanceId) &&
      !string.Equals(
          after.ManagerInstanceId,
          entry.ResolvedManagerInstanceId,
          StringComparison.Ordinal) &&
      after.Generation == entry.ResolvedGeneration &&
      string.Equals(
          after.DesiredStateHash,
          entry.ResolvedDesiredStateHash,
          StringComparison.OrdinalIgnoreCase);

  private static RecoveryCommandOutcome ToOutcome(
      RecoveryLedgerEntry entry) =>
      new(
          entry.CommandId,
          entry.Status ?? "indeterminate",
          entry.FailureCategory,
          entry.Message,
          entry.ResolvedManagerInstanceId,
          entry.AfterManagerInstanceId,
          entry.CompletedAt ?? entry.StartedAt);

  private RecoveryExecutionReport Rejected(
      RecoverManagerCommand command,
      string failureCategory,
      string message,
      string? beforeManagerInstanceId)
  {
    LogExecutionRejected(command.CommandId, failureCategory);
    return new RecoveryExecutionReport(
        null,
        new RecoveryCommandOutcome(
            command.CommandId,
            "rejected",
            failureCategory,
            message,
            beforeManagerInstanceId,
            null,
            _timeProvider.GetUtcNow()));
  }

  private bool IsLocallyEnabled() =>
      _options.Value.ManagerRecoveryEnabled &&
      !_executionEnvironment.IsContainer;

  [LoggerMessage(
      Level = LogLevel.Information,
      Message = "Recovery command {CommandId} started for profile {ProfileId}.")]
  private partial void LogExecutionStarted(
      Guid commandId,
      string profileId);

  [LoggerMessage(
      Level = LogLevel.Information,
      Message = "Recovery command {CommandId} replaced the manager for profile {ProfileId}.")]
  private partial void LogExecutionSucceeded(
      Guid commandId,
      string profileId);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Recovery command {CommandId} completed as {Status}: {Reason}.")]
  private partial void LogExecutionFailed(
      Guid commandId,
      string status,
      string reason);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Recovery command {CommandId} was rejected locally: {FailureCategory}.")]
  private partial void LogExecutionRejected(
      Guid commandId,
      string failureCategory);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Recovery command {CommandId} was already recorded locally and was not executed again.")]
  private partial void LogDuplicateSuppressed(Guid commandId);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Interrupted recovery command {CommandId} resolved as {Status}.")]
  private partial void LogInterruptedResolved(
      Guid commandId,
      string status);

  [LoggerMessage(
      Level = LogLevel.Error,
      Message = "The local recovery ledger could not be accessed: {Reason}")]
  private partial void LogLedgerFailure(string reason);
}
