using System.ComponentModel;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

[DoNotAutoRegister]
internal sealed partial class CapacityCommandExecutor(
    CapacityProfileResolver _profileResolver,
    LocalProfileOperationGate _operationGate,
    ISetupProcessRunner _processRunner,
    IOptions<ConnectorOptions> _options,
    TimeProvider _timeProvider,
    ILogger<CapacityCommandExecutor> _logger)
{
  public Task<CapacityOperatorCapability?> ReadCapabilityAsync(
      CancellationToken cancellationToken) =>
      _profileResolver.ReadCapabilityAsync(cancellationToken);

  public async Task<CapacityCommandOutcome> ExecuteAsync(
      SetCapacityCommand command,
      CancellationToken cancellationToken)
  {
    var completedAt = _timeProvider.GetUtcNow();
    if (!_options.Value.OperatorModeEnabled)
    {
      return Rejected(
          command,
          "Capacity operator mode is disabled.",
          completedAt);
    }
    if (command.ExpiresAt <= completedAt)
    {
      return Rejected(
          command,
          "Capacity command expired before execution.",
          completedAt);
    }

    using var lease = _operationGate.AcquireOrNull(command.ProfileId);
    if (lease is null)
    {
      return Rejected(
          command,
          "Another local profile operation is already active.",
          _timeProvider.GetUtcNow());
    }

    var resolution = await _profileResolver.ResolveAsync(
        command.ProfileId,
        cancellationToken);
    if (resolution.Profile is null)
    {
      return Rejected(
          command,
          resolution.Error ?? "Profile is unavailable.",
          _timeProvider.GetUtcNow());
    }
    var profile = resolution.Profile;
    if (profile.Generation != command.ExpectedGeneration)
    {
      return Rejected(
          command,
          "Profile generation changed before execution.",
          _timeProvider.GetUtcNow());
    }
    if (command.Maximum < 1 ||
        command.Maximum > profile.MaximumAllowed)
    {
      return Rejected(
          command,
          "Requested maximum violates local capacity policy.",
          _timeProvider.GetUtcNow());
    }

    SetupProcessResult result;
    try
    {
      result = await _processRunner.RunAsync(
          new SetupProcessRequest(
              _options.Value.PowerShellExecutable,
              Path.GetFullPath(_options.Value.PitCrewRoot),
              BuildArguments(profile, command.Maximum),
              TimeSpan.FromSeconds(
                  _options.Value.CapacityCommandTimeoutSeconds)),
          cancellationToken);
    }
    catch (Win32Exception exception)
    {
      LogExecutionFailed(command.CommandId, exception.Message);
      return Failed(
          command,
          "The local PowerShell process could not be started.",
          _timeProvider.GetUtcNow());
    }
    catch (IOException exception)
    {
      LogExecutionFailed(command.CommandId, exception.Message);
      return Failed(
          command,
          "The local PitCrew operation could not be executed.",
          _timeProvider.GetUtcNow());
    }
    catch (InvalidOperationException exception)
    {
      LogExecutionFailed(command.CommandId, exception.Message);
      return Failed(
          command,
          "The local PitCrew operation could not be constructed.",
          _timeProvider.GetUtcNow());
    }
    if (result.TimedOut)
    {
      LogExecutionFailed(command.CommandId, "timed out");
      return Failed(
          command,
          "PitCrew capacity update timed out.",
          _timeProvider.GetUtcNow());
    }
    if (result.ExitCode != 0)
    {
      LogExecutionFailed(
          command.CommandId,
          $"exited with code {result.ExitCode?.ToString() ?? "unknown"}");
      return Failed(
          command,
          "PitCrew rejected the capacity update.",
          _timeProvider.GetUtcNow());
    }

    var refreshed = await _profileResolver.ResolveAsync(
        command.ProfileId,
        cancellationToken);
    if (refreshed.Profile is null ||
        refreshed.Profile.CurrentMaximum != command.Maximum ||
        refreshed.Profile.Generation <= command.ExpectedGeneration)
    {
      LogExecutionFailed(
          command.CommandId,
          "did not produce the expected acknowledged state");
      return Failed(
          command,
          "PitCrew did not acknowledge the requested capacity.",
          _timeProvider.GetUtcNow());
    }

    LogExecutionSucceeded(
        command.CommandId,
        command.ProfileId,
        command.Maximum);
    return new CapacityCommandOutcome(
        command.CommandId,
        "succeeded",
        "Capacity maximum was acknowledged.",
        refreshed.Profile.Generation,
        _timeProvider.GetUtcNow());
  }

  internal static IReadOnlyList<string> BuildArguments(
      CapacityProfileDefinition profile,
      int maximum)
  {
    var arguments = new List<string>
    {
      "-NoProfile",
      "-File",
      Path.Combine("Setup-Runner.ps1"),
      "-Profile",
      profile.ProfileId,
      "-CapacityOnly",
      "-Image",
      profile.Image,
      $"-PullImage:${profile.PullImage.ToString().ToLowerInvariant()}",
      "-Labels",
      string.Join(',', profile.Labels),
      "-NamePrefix",
      profile.NamePrefix,
      $"-Autoscale:${profile.Autoscale.ToString().ToLowerInvariant()}",
    };
    if (!string.IsNullOrWhiteSpace(profile.RunnerGroup))
    {
      arguments.Add("-RunnerGroup");
      arguments.Add(profile.RunnerGroup);
    }
    if (profile.Autoscale)
    {
      arguments.Add("-MinimumIdle");
      arguments.Add(profile.MinimumIdle.ToString(
          System.Globalization.CultureInfo.InvariantCulture));
      arguments.Add("-ScaleDownDelaySeconds");
      arguments.Add(profile.ScaleDownDelaySeconds.ToString(
          System.Globalization.CultureInfo.InvariantCulture));
    }
    arguments.Add("-Scope");
    arguments.Add(profile.Scope);
    switch (profile.Scope)
    {
      case "repo":
        if (string.IsNullOrWhiteSpace(profile.RepositoryUrl))
        {
          throw new InvalidOperationException(
              "Repository capacity profile has no local target.");
        }
        arguments.Add("-AddRepos");
        arguments.Add(
            $"{profile.RepositoryUrl}={maximum.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        break;
      case "org":
        arguments.Add("-OrgName");
        arguments.Add(profile.Organization);
        arguments.Add("-Replicas");
        arguments.Add(maximum.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        break;
      case "ent":
        arguments.Add("-EnterpriseName");
        arguments.Add(profile.Enterprise);
        arguments.Add("-Replicas");
        arguments.Add(maximum.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        break;
      default:
        throw new InvalidOperationException(
            $"Unsupported local profile scope '{profile.Scope}'.");
    }
    return arguments;
  }

  private static CapacityCommandOutcome Rejected(
      SetCapacityCommand command,
      string message,
      DateTimeOffset completedAt) =>
      new(
          command.CommandId,
          "rejected",
          message,
          null,
          completedAt);

  private static CapacityCommandOutcome Failed(
      SetCapacityCommand command,
      string message,
      DateTimeOffset completedAt) =>
      new(
          command.CommandId,
          "failed",
          message,
          null,
          completedAt);

  [LoggerMessage(
      Level = LogLevel.Information,
      Message = "Capacity command {CommandId} set profile {ProfileId} maximum to {Maximum}.")]
  private partial void LogExecutionSucceeded(
      Guid commandId,
      string profileId,
      int maximum);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Capacity command {CommandId} failed: {Reason}.")]
  private partial void LogExecutionFailed(
      Guid commandId,
      string reason);
}
