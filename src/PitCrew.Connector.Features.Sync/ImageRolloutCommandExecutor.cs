using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Executes typed profile-image rollout commands at most once through
/// PitCrew's first-class local setup operation.
/// </summary>
internal sealed partial class ImageRolloutCommandExecutor(
    ImageRolloutProfileResolver _profileResolver,
    ImageRolloutLedger _ledger,
    ImageRolloutManifestBuilder _manifestBuilder,
    LocalProfileOperationGate _operationGate,
    ISetupProcessRunner _processRunner,
    IHostExecutionEnvironment _executionEnvironment,
    IOptions<ConnectorOptions> _options,
    TimeProvider _timeProvider,
    ILogger<ImageRolloutCommandExecutor> _logger)
{
  /// <summary>
  /// Reads the profile-image rollout capability advertised to the dashboard.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels the read.</param>
  /// <returns>
  /// The capability, or <see langword="null"/> when rollout is locally disabled.
  /// </returns>
  public Task<ImageRolloutOperatorCapability?> ReadCapabilityAsync(
      CancellationToken cancellationToken) =>
      _profileResolver.ReadCapabilityAsync(cancellationToken);

  /// <summary>
  /// Resolves every attempt that started but never reached a terminal state.
  /// The resolver runs unconditionally so a durable started ledger entry can
  /// never remain permanently active if the operator later disables rollout,
  /// removes the profile from the allowlist, or the profile state becomes
  /// unavailable. Each unresolved entry terminalizes as succeeded only with
  /// exact proof, failed when the pre-operation state is positively proven
  /// unchanged, and indeterminate otherwise.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels resolution.</param>
  /// <returns>Terminal outcomes proved or classified as indeterminate.</returns>
  public async Task<IReadOnlyList<ImageRolloutCommandOutcome>> ResolveInterruptedAsync(
      CancellationToken cancellationToken)
  {
    if (!_options.Value.ImageRolloutEnabled &&
        !Path.IsPathFullyQualified(_options.Value.ImageRolloutStatePath))
    {
      return [];
    }

    IReadOnlyList<ImageRolloutLedgerEntry> unresolved;
    try
    {
      unresolved = await _ledger.ReadUnresolvedAsync(cancellationToken);
    }
    catch (IOException)
    {
      LogLedgerFailure();
      return [];
    }
    catch (UnauthorizedAccessException)
    {
      LogLedgerFailure();
      return [];
    }

    var outcomes = new List<ImageRolloutCommandOutcome>();
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
    if (outcomes.Count > 0)
    {
      await PruneManifestHistoryBestEffortAsync(cancellationToken);
    }
    return outcomes;
  }

  /// <summary>
  /// Executes one typed profile-image rollout command at most once.
  /// </summary>
  /// <param name="command">Typed command and expected fences.</param>
  /// <param name="cancellationToken">Token that cancels execution.</param>
  /// <returns>The durable progress and terminal outcome to report.</returns>
  public async Task<ImageRolloutExecutionReport> ExecuteAsync(
      RollOutProfileImageCommand command,
      CancellationToken cancellationToken)
  {
    if (!IsLocallyEnabled())
    {
      return Rejected(
          command,
          "not-allowed",
          "Profile image rollout is disabled on this connector.");
    }

    ImageRolloutLedgerEntry? recorded;
    try
    {
      recorded = await _ledger.FindAsync(command.CommandId, cancellationToken);
    }
    catch (IOException)
    {
      LogLedgerFailure();
      return Rejected(
          command,
          "unknown",
          "The local image rollout ledger could not be read.");
    }
    catch (UnauthorizedAccessException)
    {
      LogLedgerFailure();
      return Rejected(
          command,
          "unknown",
          "The local image rollout ledger could not be read.");
    }
    if (recorded is not null)
    {
      return await ReportRecordedAsync(recorded, cancellationToken);
    }

    var now = _timeProvider.GetUtcNow();
    if (command.ExpiresAt <= now)
    {
      return Rejected(
          command,
          "expired",
          "Profile image rollout command expired before execution.");
    }
    if (command.ExpiresAt - command.RequestedAt >
        TimeSpan.FromSeconds(
            _options.Value.ImageRolloutCommandMaximumExpirySeconds))
    {
      return Rejected(
          command,
          "not-allowed",
          "Profile image rollout command lifetime exceeds local policy.");
    }
    if (!ImageRolloutRecipePolicy.IsValidRecipeId(command.RecipeId))
    {
      return Rejected(
          command,
          "recipe-not-allowed",
          "The recipe id is not a supported local rollout recipe.");
    }
    var recipePolicy = _options.Value.ImageRolloutRecipes
        .FirstOrDefault(entry => string.Equals(
            entry.RecipeId,
            command.RecipeId,
            StringComparison.OrdinalIgnoreCase));
    if (recipePolicy is null)
    {
      return Rejected(
          command,
          "recipe-not-allowed",
          "The recipe is not permitted by local image rollout policy.");
    }
    var registryRepository = recipePolicy.RegistryRepository;
    if (string.IsNullOrEmpty(registryRepository) ||
        !ImageRolloutRecipePolicy.IsValidRegistryRepository(registryRepository))
    {
      // The recipe id is allowlisted but the configured registry
      // repository is missing or invalid at execution time. Surface as
      // the distinct registry-not-allowed category (never expose the
      // registry repository value itself in the outcome).
      return Rejected(
          command,
          "registry-not-allowed",
          "The local registry repository for this recipe is missing or invalid.");
    }
    if (!IsSupportedDigest(command.TargetDigest))
    {
      return Rejected(
          command,
          "not-allowed",
          "Target digest is not a supported immutable sha256 reference.");
    }

    using var lease = _operationGate.AcquireOrNull(command.ProfileId);
    if (lease is null)
    {
      return Rejected(
          command,
          "operation-active",
          "Another local profile operation is already active.");
    }

    var resolution = await _profileResolver.ResolveAsync(
        command.ProfileId,
        cancellationToken);
    if (resolution.Profile is null)
    {
      return Rejected(
          command,
          resolution.FailureCategory ?? "unknown",
          resolution.Error ?? "Profile is unavailable for rollout.");
    }
    var profile = resolution.Profile;
    // Any locally advertised failure category — schema, stale observed state,
    // or a future closed category — rejects with the same exact category the
    // dashboard already sees on the wire.
    if (profile.LocalFailureCategory is { } localFailureCategory)
    {
      return Rejected(
          command,
          localFailureCategory,
          "Local capability signals rollout is not currently permitted.");
    }
    if (!string.Equals(
            profile.Architecture,
            command.TargetPlatform,
            StringComparison.Ordinal))
    {
      return Rejected(
          command,
          "unsupported-architecture",
          "The target platform is unsupported on this connector.");
    }
    if (profile.ObservedStateAgeSeconds >
        _options.Value.ImageRolloutObservedStateMaximumAgeSeconds)
    {
      return Rejected(
          command,
          "stale-fence",
          "Observed profile state is older than local policy allows.");
    }
    if (!FencesMatch(command, profile))
    {
      return Rejected(
          command,
          "stale-fence",
          "Local profile state changed before execution.");
    }

    string localManifestPath;
    try
    {
      localManifestPath = _manifestBuilder.BuildAndWriteManifest(
          command.CommandId,
          profile.ProfileId,
          profile.StaticProfileJson,
          registryRepository,
          command.TargetDigest);
    }
    catch (InvalidDataException)
    {
      LogManifestFailure(command.CommandId);
      return Rejected(
          command,
          "unsupported-schema",
          "The local profile manifest could not be reconstructed exactly.");
    }
    catch (JsonException)
    {
      LogManifestFailure(command.CommandId);
      return Rejected(
          command,
          "unsupported-schema",
          "The local profile manifest could not be reconstructed exactly.");
    }
    catch (IOException)
    {
      LogManifestFailure(command.CommandId);
      return Rejected(
          command,
          "unknown",
          "The local profile manifest could not be written.");
    }
    catch (UnauthorizedAccessException)
    {
      LogManifestFailure(command.CommandId);
      return Rejected(
          command,
          "unknown",
          "The local profile manifest could not be written.");
    }

    var startedAt = _timeProvider.GetUtcNow();
    var entry = new ImageRolloutLedgerEntry(
        command.CommandId,
        profile.ProfileId,
        command.CandidateId,
        command.RecipeId,
        command.TargetDigest,
        command.TargetPlatform,
        registryRepository,
        localManifestPath,
        command.ExpectedCurrentImageReference,
        command.ExpectedCurrentImageDigest,
        command.ExpectedCurrentLocalImageId,
        command.ExpectedCurrentWorkerRevision,
        command.ExpectedStaticFingerprint,
        command.ExpectedPreservedConfigurationFingerprint,
        command.ExpectedRoutingFingerprint,
        command.ExpectedDesiredGeneration,
        command.ExpectedDesiredStateHash,
        profile.CurrentWorkerRevision,
        profile.ManifestSourcePath,
        startedAt,
        ImageRolloutLedgerPhases.Started,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);
    bool claimed;
    try
    {
      claimed = await _ledger.RecordStartedAsync(entry, cancellationToken);
    }
    catch (IOException)
    {
      LogLedgerFailure();
      return Rejected(
          command,
          "unknown",
          "The local image rollout ledger could not be written.");
    }
    catch (UnauthorizedAccessException)
    {
      LogLedgerFailure();
      return Rejected(
          command,
          "unknown",
          "The local image rollout ledger could not be written.");
    }
    if (!claimed)
    {
      var duplicate = await _ledger.FindAsync(command.CommandId, cancellationToken);
      return duplicate is null
          ? Rejected(
              command,
              "unknown",
              "The local image rollout ledger could not be read.")
          : await ReportRecordedAsync(duplicate, cancellationToken);
    }

    var progress = new ImageRolloutCommandProgress(
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
              BuildArguments(profile, localManifestPath),
              TimeSpan.FromSeconds(
                  _options.Value.ImageRolloutCommandTimeoutSeconds + 60)),
          cancellationToken);
    }
    catch (Win32Exception)
    {
      // Never persist or transmit the raw Win32Exception message; use a
      // bounded closed literal so the outcome carries no host detail.
      return await CompleteAsync(
          entry,
          progress,
          "failed",
          "process-failure",
          "The local PowerShell process could not be started.",
          null,
          "degraded",
          null,
          null,
          LastErrorProcessStart,
          cancellationToken);
    }
    catch (IOException)
    {
      return await CompleteAsync(
          entry,
          progress,
          "failed",
          "process-failure",
          "The local PitCrew rollout operation could not be executed.",
          null,
          "degraded",
          null,
          null,
          LastErrorProcessIo,
          cancellationToken);
    }
    catch (InvalidOperationException)
    {
      // BuildArguments refused to project a safe Setup-Runner invocation
      // (missing routing identity, unsupported scope, or unrepresentable
      // repo state). Fail closed as unsupported-topology so nothing runs
      // and the exact rejection reason is distinguishable from a schema
      // failure. The exception message is not persisted or logged because
      // it can contain host-specific detail.
      LogExecutionFailed(command.CommandId, "unsupported-topology");
      return await CompleteAsync(
          entry,
          progress,
          "failed",
          "unsupported-topology",
          "The local routing state cannot be preserved by Setup-Runner.",
          null,
          "degraded",
          null,
          null,
          LastErrorUnsupportedRouting,
          cancellationToken);
    }

    var after = await _profileResolver.ResolveAsync(
        command.ProfileId,
        cancellationToken);
    var afterProfile = after.Profile;

    if (result.TimedOut)
    {
      var timedTerminal = ClassifyPostState(entry, afterProfile);
      return timedTerminal switch
      {
        PostStateClassification.Succeeded => await CompleteAsync(
            entry,
            progress,
            "succeeded",
            null,
            "PitCrew applied the target digest before the local timeout elapsed.",
            afterProfile?.CurrentWorkerRevision,
            afterProfile?.ManagerConvergenceStatus ?? "rolling",
            afterProfile?.CurrentWorkers,
            afterProfile?.StaleWorkers,
            LastErrorTimedOut,
            cancellationToken),
        PostStateClassification.Unchanged => await CompleteAsync(
            entry,
            progress,
            "failed",
            "timeout",
            "PitCrew did not apply the target digest before the local timeout.",
            afterProfile?.CurrentWorkerRevision,
            afterProfile?.ManagerConvergenceStatus ?? "degraded",
            afterProfile?.CurrentWorkers,
            afterProfile?.StaleWorkers,
            LastErrorTimedOut,
            cancellationToken),
        _ => await CompleteAsync(
            entry,
            progress,
            "indeterminate",
            "timeout",
            "PitCrew rollout exceeded the local timeout without provable postconditions.",
            afterProfile?.CurrentWorkerRevision,
            afterProfile?.ManagerConvergenceStatus ?? "degraded",
            afterProfile?.CurrentWorkers,
            afterProfile?.StaleWorkers,
            LastErrorTimedOut,
            cancellationToken),
      };
    }
    if (result.ExitCode != 0)
    {
      var terminal = ClassifyPostState(entry, afterProfile);
      var exitCodeEvidence = FormatExitCode(result.ExitCode);
      return terminal switch
      {
        PostStateClassification.Succeeded => await CompleteAsync(
            entry,
            progress,
            "succeeded",
            null,
            "PitCrew applied the target digest after a non-zero exit.",
            afterProfile?.CurrentWorkerRevision,
            afterProfile?.ManagerConvergenceStatus ?? "rolling",
            afterProfile?.CurrentWorkers,
            afterProfile?.StaleWorkers,
            exitCodeEvidence,
            cancellationToken),
        PostStateClassification.Unchanged => await CompleteAsync(
            entry,
            progress,
            "failed",
            "process-failure",
            "PitCrew did not apply the target digest and preserved existing state.",
            afterProfile?.CurrentWorkerRevision,
            afterProfile?.ManagerConvergenceStatus ?? "degraded",
            afterProfile?.CurrentWorkers,
            afterProfile?.StaleWorkers,
            exitCodeEvidence,
            cancellationToken),
        _ => await CompleteAsync(
            entry,
            progress,
            "indeterminate",
            "process-failure",
            "PitCrew reported a non-zero exit without provable postconditions.",
            afterProfile?.CurrentWorkerRevision,
            afterProfile?.ManagerConvergenceStatus ?? "degraded",
            afterProfile?.CurrentWorkers,
            afterProfile?.StaleWorkers,
            exitCodeEvidence,
            cancellationToken),
      };
    }

    var success = ClassifyPostState(entry, afterProfile);
    return success switch
    {
      PostStateClassification.Succeeded => await CompleteAsync(
          entry,
          progress,
          "succeeded",
          null,
          "PitCrew applied the target digest and preserved supported workers.",
          afterProfile?.CurrentWorkerRevision,
          afterProfile?.ManagerConvergenceStatus ?? "current",
          afterProfile?.CurrentWorkers,
          afterProfile?.StaleWorkers,
          null,
          cancellationToken),
      PostStateClassification.Unchanged => await CompleteAsync(
          entry,
          progress,
          "failed",
          "process-failure",
          "PitCrew reported success without applying the target digest.",
          afterProfile?.CurrentWorkerRevision,
          afterProfile?.ManagerConvergenceStatus ?? "degraded",
          afterProfile?.CurrentWorkers,
          afterProfile?.StaleWorkers,
          LastErrorPostconditionsUnverified,
          cancellationToken),
      _ => await CompleteAsync(
          entry,
          progress,
          "indeterminate",
          "unknown",
          "PitCrew reported success without locally provable postconditions.",
          afterProfile?.CurrentWorkerRevision,
          afterProfile?.ManagerConvergenceStatus ?? "degraded",
          afterProfile?.CurrentWorkers,
          afterProfile?.StaleWorkers,
          "postconditions were not provable",
          cancellationToken),
    };
  }

  /// <summary>
  /// Prunes rollout manifest history that is not referenced by any current
  /// success or indeterminate ledger entry. Ledger entries themselves are
  /// never pruned; they remain as durable at-most-once tombstones so a
  /// redelivered command id cannot execute a second time.
  /// </summary>
  public async Task PruneManifestHistoryAsync(
      CancellationToken cancellationToken)
  {
    if (!IsLocallyEnabled())
    {
      return;
    }
    await PruneManifestHistoryBestEffortAsync(cancellationToken);
  }

  private async Task PruneManifestHistoryBestEffortAsync(
      CancellationToken cancellationToken)
  {
    // Collect currently applied static-profile manifest source paths from
    // every allowlisted profile: these must never be pruned even when they
    // are not referenced by a started ledger entry (i.e. after a
    // previously-successful rollout has terminalized).
    var protectedPaths = new List<string>();
    foreach (var allowedProfileId in _options.Value.AllowedImageRolloutProfiles)
    {
      // Best-effort read; PruneManifestHistoryBestEffort must not throw and
      // must not block terminal handling because the resolver could not
      // read one profile.
      try
      {
        var resolution = await _profileResolver.ResolveAsync(
            allowedProfileId,
            cancellationToken);
        if (resolution.Profile?.ManifestSourcePath is { } sourcePath &&
            !string.IsNullOrWhiteSpace(sourcePath))
        {
          protectedPaths.Add(sourcePath);
        }
      }
      catch (IOException)
      {
        LogLedgerFailure();
      }
      catch (UnauthorizedAccessException)
      {
        LogLedgerFailure();
      }
    }

    IReadOnlySet<string> referenced;
    try
    {
      referenced = _ledger.EnumerateReferencedManifestPaths(
          protectedPaths,
          _options.Value.ImageRolloutRetainedManifests);
    }
    catch (IOException)
    {
      LogLedgerFailure();
      return;
    }
    catch (UnauthorizedAccessException)
    {
      LogLedgerFailure();
      return;
    }
    try
    {
      _manifestBuilder.PruneOrphans(referenced);
    }
    catch (IOException)
    {
      LogLedgerFailure();
    }
    catch (UnauthorizedAccessException)
    {
      LogLedgerFailure();
    }

    // Ledger entries are intentionally not pruned here. Each ledger entry is
    // the durable at-most-once tombstone for its command id; deleting terminal
    // entries would allow a previously-executed command to run a second time
    // if the manager redelivered it after cleanup. Only orphan generated
    // manifests are bounded above; the ledger keeps every started and every
    // terminal record permanently.
  }

  internal static IReadOnlyList<string> BuildArguments(
      ImageRolloutProfileState profile,
      string localManifestPath) =>
      BuildArguments(
          profile.ProfileId,
          localManifestPath,
          profile.NamePrefix,
          profile.Routing);

  internal static IReadOnlyList<string> BuildArguments(
      string profileId,
      string localManifestPath,
      ImageRolloutRoutingState routing) =>
      BuildArguments(
          profileId,
          localManifestPath,
          namePrefix: null,
          routing);

  internal static IReadOnlyList<string> BuildArguments(
      string profileId,
      string localManifestPath,
      string? namePrefix,
      ImageRolloutRoutingState routing)
  {
    // Setup-Runner requires a scope-appropriate invocation. Repo scope with
    // no repo targets fails unless -Pause; org/ent require identity + either
    // -Replicas or -Pause. Rebuilding the invocation only from local values
    // means Dashboard never observes the routing surface on the wire, and
    // capacity-only interpretations later cannot reintroduce a legacy
    // manifest.
    var arguments = new List<string>
    {
      "-NoProfile",
      "-File",
      Path.Combine("Setup-Runner.ps1"),
      "-Profile",
      profileId,
      "-ProfilePath",
      localManifestPath,
    };
    // NamePrefix is a locally read configuration value, but it is not a
    // runner-profile.schema.json property; it stays in the CLI so the
    // generated manifest never invents a non-schema field.
    if (!string.IsNullOrWhiteSpace(namePrefix))
    {
      arguments.Add("-NamePrefix");
      arguments.Add(namePrefix);
    }
    switch (routing.Scope)
    {
      case "repo":
        arguments.Add("-Scope");
        arguments.Add("repo");
        // Protocol v11: exactly one repository target must be present.
        // Zero-count target => -Pause; positive-count => one
        // -AddRepos url=count pair. Multi-target and empty repo routing
        // are refused at the routing projection boundary as
        // `unsupported-topology`, but assert defensively here so the
        // CLI is never invoked with a shape PowerShell -File binding
        // rejects (`-AddRepos` cannot be specified more than once, and
        // adjacent/comma-joined values cannot bind multiple entries).
        // This mirrors the existing single-target capacity protocol.
        if (routing.RepositoryTargets.Count != 1)
        {
          throw new InvalidOperationException(
              "Repository rollout requires exactly one repository target " +
              "in protocol v11.");
        }
        var target = routing.RepositoryTargets[0];
        if (target.Workers == 0)
        {
          // The single target is fully paused. Emit -Pause and no
          // -AddRepos so Setup-Runner's repo-scope pause path fires.
          arguments.Add("-Pause");
        }
        else
        {
          arguments.Add("-AddRepos");
          arguments.Add(
              $"{target.Url}={target.Workers.ToString(
                  System.Globalization.CultureInfo.InvariantCulture)}");
        }
        break;
      case "org":
        arguments.Add("-Scope");
        arguments.Add("org");
        if (string.IsNullOrWhiteSpace(routing.Organization))
        {
          throw new InvalidOperationException(
              "Org-scope rollout has no local organization identity.");
        }
        arguments.Add("-OrgName");
        arguments.Add(routing.Organization);
        if (routing.Paused)
        {
          arguments.Add("-Pause");
        }
        else
        {
          if (routing.Replicas is null)
          {
            throw new InvalidOperationException(
                "Org-scope rollout has no local replicas count.");
          }
          arguments.Add("-Replicas");
          arguments.Add(routing.Replicas.Value.ToString(
              System.Globalization.CultureInfo.InvariantCulture));
        }
        break;
      case "ent":
        arguments.Add("-Scope");
        arguments.Add("ent");
        if (string.IsNullOrWhiteSpace(routing.Enterprise))
        {
          throw new InvalidOperationException(
              "Enterprise-scope rollout has no local enterprise identity.");
        }
        arguments.Add("-EnterpriseName");
        arguments.Add(routing.Enterprise);
        if (routing.Paused)
        {
          arguments.Add("-Pause");
        }
        else
        {
          if (routing.Replicas is null)
          {
            throw new InvalidOperationException(
                "Enterprise-scope rollout has no local replicas count.");
          }
          arguments.Add("-Replicas");
          arguments.Add(routing.Replicas.Value.ToString(
              System.Globalization.CultureInfo.InvariantCulture));
        }
        break;
      default:
        throw new InvalidOperationException(
            $"Unsupported local rollout routing scope '{routing.Scope}'.");
    }
    return arguments;
  }

  /// <summary>
  /// Bounded closed literal values used for outcome/ledger
  /// <c>LastError</c>. Raw exception messages, JSON parser text, and
  /// process output are never persisted or transmitted; every emitted
  /// value is guaranteed to be one of these constants (or a
  /// <see cref="LastErrorExitCodePrefix"/> value composed with a bounded
  /// integer). See <see cref="MaxLastErrorLength"/> for the enforced cap.
  /// </summary>
  internal const int MaxLastErrorLength = 64;
  internal const string LastErrorProcessStart = "process-start-failed";
  internal const string LastErrorProcessIo = "process-io-failed";
  internal const string LastErrorUnsupportedRouting = "unsupported-routing";
  internal const string LastErrorTimedOut = "timed-out";
  internal const string LastErrorPostconditionsUnverified =
      "postconditions-not-applied";
  internal const string LastErrorExitCodePrefix = "exit-code:";
  internal const string LastErrorExitCodeUnknown = "exit-code:unknown";

  /// <summary>
  /// Composes an <see cref="LastErrorExitCodePrefix"/> literal with a
  /// bounded numeric exit code so the outcome carries no free-form host
  /// text. Unknown exit codes collapse to
  /// <see cref="LastErrorExitCodeUnknown"/>.
  /// </summary>
  private static string FormatExitCode(int? exitCode) =>
      exitCode is null
          ? LastErrorExitCodeUnknown
          : string.Concat(
              LastErrorExitCodePrefix,
              exitCode.Value.ToString(CultureInfo.InvariantCulture));

  /// <summary>
  /// Normalizes a local resolver/executor failure category into the closed
  /// set accepted by the protocol outcome contract
  /// (<see cref="SyncConnectorUnitOfWork.IsValidImageRolloutOutcome"/>).
  /// </summary>
  private static string NormalizeOutcomeFailureCategory(string category) =>
      category switch
      {
        // Wire-permitted outcome categories pass through unchanged.
        "expired" or
        "not-allowed" or
        "recipe-not-allowed" or
        "registry-not-allowed" or
        "stale-fence" or
        "unsupported" or
        "unsupported-architecture" or
        "unsupported-topology" or
        "operation-active" or
        "timeout" or
        "process-failure" or
        "unknown" => category,
        // Local capability-only categories map to the closed outcome set.
        "stale-observed-state" => "stale-fence",
        "unsupported-schema" or "unsupported-manager" => "unsupported",
        "policy-disabled" => "not-allowed",
        // Any unmapped category collapses to "unknown" so the outcome remains
        // wire-valid instead of failing IsValidImageRolloutOutcome.
        _ => "unknown",
      };

  private async Task<ImageRolloutCommandOutcome?> ResolveInterruptedEntryAsync(
      ImageRolloutLedgerEntry entry,
      CancellationToken cancellationToken)
  {
    var after = await _profileResolver.ResolveAsync(
        entry.ProfileId,
        cancellationToken);
    var afterProfile = after.Profile;
    var classification = ClassifyPostState(entry, afterProfile);
    var completedAt = _timeProvider.GetUtcNow();
    var resolved = entry with
    {
      Phase = ImageRolloutLedgerPhases.Terminal,
      Status = classification switch
      {
        PostStateClassification.Succeeded => "succeeded",
        PostStateClassification.Unchanged => "failed",
        _ => "indeterminate",
      },
      FailureCategory = classification switch
      {
        PostStateClassification.Succeeded => null,
        PostStateClassification.Unchanged => "process-failure",
        _ => "unknown",
      },
      Message = classification switch
      {
        PostStateClassification.Succeeded =>
            "An interrupted rollout is proved by an applied target digest.",
        PostStateClassification.Unchanged =>
            "An interrupted rollout is proved unchanged and was not repeated.",
        _ =>
            "An interrupted rollout could not be proved and was not repeated.",
      },
      TargetWorkerRevision = afterProfile?.CurrentWorkerRevision,
      ManagerConvergenceStatus = afterProfile?.ManagerConvergenceStatus ??
          "degraded",
      CurrentWorkers = afterProfile?.CurrentWorkers,
      StaleWorkers = afterProfile?.StaleWorkers,
      LastError = null,
      CompletedAt = completedAt,
    };
    try
    {
      await _ledger.RecordTerminalAsync(resolved, cancellationToken);
    }
    catch (IOException)
    {
      LogLedgerFailure();
      return null;
    }
    catch (UnauthorizedAccessException)
    {
      LogLedgerFailure();
      return null;
    }
    LogInterruptedResolved(entry.CommandId, resolved.Status!);
    return ToOutcome(resolved);
  }

  private async Task<ImageRolloutExecutionReport> ReportRecordedAsync(
      ImageRolloutLedgerEntry recorded,
      CancellationToken cancellationToken)
  {
    LogDuplicateSuppressed(recorded.CommandId);
    if (string.Equals(
            recorded.Phase,
            ImageRolloutLedgerPhases.Terminal,
            StringComparison.Ordinal))
    {
      return new ImageRolloutExecutionReport(null, ToOutcome(recorded));
    }
    var resolved = await ResolveInterruptedEntryAsync(
        recorded,
        cancellationToken);
    return new ImageRolloutExecutionReport(
        null,
        resolved ?? new ImageRolloutCommandOutcome(
            recorded.CommandId,
            "indeterminate",
            "unknown",
            "A previously started rollout could not be resolved locally.",
            recorded.TargetDigest,
            recorded.ResolvedPreOperationRevision,
            "degraded",
            null,
            null,
            null,
            _timeProvider.GetUtcNow()));
  }

  private async Task<ImageRolloutExecutionReport> CompleteAsync(
      ImageRolloutLedgerEntry entry,
      ImageRolloutCommandProgress progress,
      string status,
      string? failureCategory,
      string message,
      string? targetWorkerRevision,
      string managerConvergenceStatus,
      int? currentWorkers,
      int? staleWorkers,
      string? lastError,
      CancellationToken cancellationToken)
  {
    var normalizedCategory = failureCategory is null
        ? null
        : NormalizeOutcomeFailureCategory(failureCategory);
    var completed = entry with
    {
      Phase = ImageRolloutLedgerPhases.Terminal,
      Status = status,
      FailureCategory = normalizedCategory,
      Message = message,
      TargetWorkerRevision = targetWorkerRevision,
      ManagerConvergenceStatus = managerConvergenceStatus,
      CurrentWorkers = currentWorkers,
      StaleWorkers = staleWorkers,
      LastError = lastError,
      CompletedAt = _timeProvider.GetUtcNow(),
    };
    try
    {
      await _ledger.RecordTerminalAsync(completed, cancellationToken);
    }
    catch (IOException)
    {
      LogLedgerFailure();
    }
    catch (UnauthorizedAccessException)
    {
      LogLedgerFailure();
    }
    if (lastError is null && string.Equals(status, "succeeded", StringComparison.Ordinal))
    {
      LogExecutionSucceeded(entry.CommandId, entry.ProfileId);
    }
    else
    {
      LogExecutionCompleted(entry.CommandId, status);
    }
    // Prune orphaned manifest history after every terminal transition so the
    // state root does not accumulate stale reconstructions between restarts.
    // The currently referenced/succeeded/indeterminate manifests (bounded to
    // the newest terminal-retention cap) plus every started manifest and the
    // live static-profile manifest source paths remain safe because
    // EnumerateReferencedManifestPaths includes them. Ledger entry files are
    // intentionally not touched here so the at-most-once command-id
    // tombstones stay durable.
    await PruneManifestHistoryBestEffortAsync(cancellationToken);
    return new ImageRolloutExecutionReport(progress, ToOutcome(completed));
  }

  private static ImageRolloutCommandOutcome ToOutcome(
      ImageRolloutLedgerEntry entry) =>
      new(
          entry.CommandId,
          entry.Status ?? "indeterminate",
          entry.FailureCategory,
          entry.Message,
          entry.TargetDigest,
          entry.TargetWorkerRevision,
          entry.ManagerConvergenceStatus ?? "degraded",
          entry.CurrentWorkers,
          entry.StaleWorkers,
          entry.LastError,
          entry.CompletedAt ?? entry.StartedAt);

  private ImageRolloutExecutionReport Rejected(
      RollOutProfileImageCommand command,
      string failureCategory,
      string message)
  {
    var normalizedCategory = NormalizeOutcomeFailureCategory(failureCategory);
    LogExecutionRejected(command.CommandId, normalizedCategory);
    return new ImageRolloutExecutionReport(
        null,
        new ImageRolloutCommandOutcome(
            command.CommandId,
            "rejected",
            normalizedCategory,
            message,
            null,
            null,
            "degraded",
            null,
            null,
            null,
            _timeProvider.GetUtcNow()));
  }

  private static bool FencesMatch(
      RollOutProfileImageCommand command,
      ImageRolloutProfileState profile) =>
      OrdinalMatch(
          command.ExpectedCurrentImageReference,
          profile.CurrentImageReference) &&
      OrdinalIgnoreCaseMatch(
          command.ExpectedCurrentImageDigest,
          profile.CurrentImageDigest) &&
      OrdinalIgnoreCaseMatch(
          command.ExpectedCurrentLocalImageId,
          profile.CurrentLocalImageId) &&
      OrdinalMatch(
          command.ExpectedCurrentWorkerRevision,
          profile.CurrentWorkerRevision) &&
      string.Equals(
          command.ExpectedStaticFingerprint,
          profile.StaticFingerprint,
          StringComparison.OrdinalIgnoreCase) &&
      string.Equals(
          command.ExpectedPreservedConfigurationFingerprint,
          profile.PreservedConfigurationFingerprint,
          StringComparison.OrdinalIgnoreCase) &&
      string.Equals(
          command.ExpectedRoutingFingerprint,
          profile.RoutingFingerprint,
          StringComparison.OrdinalIgnoreCase) &&
      command.ExpectedDesiredGeneration == profile.DesiredGeneration &&
      OrdinalIgnoreCaseMatch(
          command.ExpectedDesiredStateHash,
          profile.DesiredStateHash);

  private static bool OrdinalMatch(string? expected, string? actual) =>
      expected is null
          ? actual is null
          : string.Equals(expected, actual, StringComparison.Ordinal);

  private static bool OrdinalIgnoreCaseMatch(string? expected, string? actual) =>
      expected is null
          ? actual is null
          : string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

  private enum PostStateClassification
  {
    Indeterminate,
    Succeeded,
    Unchanged,
  }

  private static PostStateClassification ClassifyPostState(
      ImageRolloutLedgerEntry entry,
      ImageRolloutProfileState? after)
  {
    if (after is null ||
        !after.LocalSchemaSupported ||
        after.LocalFailureCategory is not null)
    {
      return PostStateClassification.Indeterminate;
    }

    // Success requires every rollout gate to reflect the intended target:
    // the target digest is now applied, the preserved configuration
    // fingerprint is unchanged (nothing else moved), the routing
    // fingerprint is unchanged, the desired generation+hash still match
    // what the command was authorized against, a target worker revision
    // has been reported, AND the observed worker counts are available.
    // Missing worker counts would produce a "succeeded" outcome the
    // dashboard cannot accept (protocol requires non-null CurrentWorkers
    // and StaleWorkers on succeeded), so we fall through to
    // Indeterminate instead of lying about a proved success.
    var digestMatches = OrdinalIgnoreCaseMatch(
        entry.TargetDigest,
        after.CurrentImageDigest);
    var imageReferenceMatches = OrdinalMatch(
        $"{entry.RegistryRepository}@{entry.TargetDigest}",
        after.CurrentImageReference);
    var preservedMatches = OrdinalIgnoreCaseMatch(
        entry.ExpectedPreservedConfigurationFingerprint,
        after.PreservedConfigurationFingerprint);
    var routingMatches = OrdinalIgnoreCaseMatch(
        entry.ExpectedRoutingFingerprint,
        after.RoutingFingerprint);
    var generationMatches =
        entry.ExpectedDesiredGeneration == after.DesiredGeneration;
    var hashMatches = OrdinalIgnoreCaseMatch(
        entry.ExpectedDesiredStateHash,
        after.DesiredStateHash);
    var revisionChanged =
        IsSupportedRevision(after.CurrentWorkerRevision) &&
        !OrdinalMatch(
            entry.ResolvedPreOperationRevision,
            after.CurrentWorkerRevision);
    var staticFingerprintChanged =
        !OrdinalIgnoreCaseMatch(
            entry.ExpectedStaticFingerprint,
            after.StaticFingerprint);
    var postconditionsAvailable =
        after.CurrentWorkers is not null &&
        after.StaleWorkers is not null;
    if (digestMatches &&
        imageReferenceMatches &&
        preservedMatches &&
        routingMatches &&
        generationMatches &&
        hashMatches &&
        revisionChanged &&
        staticFingerprintChanged &&
        postconditionsAvailable)
    {
      return PostStateClassification.Succeeded;
    }

    // Unchanged requires proving every observable rollout fence still
    // reports the pre-operation state. If any moved (routing rewritten,
    // generation advanced, hash changed, image drifted) the observation
    // is indeterminate rather than a clean no-op.
    var staticUnchanged = OrdinalIgnoreCaseMatch(
        entry.ExpectedStaticFingerprint,
        after.StaticFingerprint);
    var preservedUnchanged = OrdinalIgnoreCaseMatch(
        entry.ExpectedPreservedConfigurationFingerprint,
        after.PreservedConfigurationFingerprint);
    var routingUnchanged = OrdinalIgnoreCaseMatch(
        entry.ExpectedRoutingFingerprint,
        after.RoutingFingerprint);
    var generationUnchanged =
        entry.ExpectedDesiredGeneration == after.DesiredGeneration;
    var hashUnchanged = OrdinalIgnoreCaseMatch(
        entry.ExpectedDesiredStateHash,
        after.DesiredStateHash);
    var imageReferenceUnchanged = OrdinalMatch(
        entry.ExpectedCurrentImageReference,
        after.CurrentImageReference);
    var imageDigestUnchanged = OrdinalIgnoreCaseMatch(
        entry.ExpectedCurrentImageDigest,
        after.CurrentImageDigest);
    var localImageIdUnchanged = OrdinalIgnoreCaseMatch(
        entry.ExpectedCurrentLocalImageId,
        after.CurrentLocalImageId);
    var revisionUnchanged =
        entry.ResolvedPreOperationRevision is not null &&
        after.CurrentWorkerRevision is not null &&
        string.Equals(
            after.CurrentWorkerRevision,
            entry.ResolvedPreOperationRevision,
            StringComparison.Ordinal);
    if (staticUnchanged &&
        preservedUnchanged &&
        routingUnchanged &&
        generationUnchanged &&
        hashUnchanged &&
        imageReferenceUnchanged &&
        imageDigestUnchanged &&
        localImageIdUnchanged &&
        revisionUnchanged)
    {
      return PostStateClassification.Unchanged;
    }
    return PostStateClassification.Indeterminate;
  }

  private static bool IsSupportedDigest(string digest)
  {
    if (string.IsNullOrEmpty(digest) ||
        !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
        digest.Length != 71)
    {
      return false;
    }
    for (var index = 7; index < digest.Length; index++)
    {
      var character = digest[index];
      if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
      {
        return false;
      }
    }
    return true;
  }

  private static bool IsSupportedRevision(string? revision)
  {
    if (revision is null || revision.Length != 64)
    {
      return false;
    }
    foreach (var character in revision)
    {
      if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
      {
        return false;
      }
    }
    return true;
  }

  private bool IsLocallyEnabled() =>
      _options.Value.ImageRolloutEnabled &&
      !_executionEnvironment.IsContainer;

  [LoggerMessage(
      Level = LogLevel.Information,
      Message = "Image rollout command {CommandId} started for profile {ProfileId}.")]
  private partial void LogExecutionStarted(
      Guid commandId,
      string profileId);

  [LoggerMessage(
      Level = LogLevel.Information,
      Message = "Image rollout command {CommandId} applied the target digest for profile {ProfileId}.")]
  private partial void LogExecutionSucceeded(
      Guid commandId,
      string profileId);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Image rollout command {CommandId} completed as {Status}.")]
  private partial void LogExecutionCompleted(
      Guid commandId,
      string status);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Image rollout command {CommandId} was rejected locally: {FailureCategory}.")]
  private partial void LogExecutionRejected(
      Guid commandId,
      string failureCategory);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Image rollout command {CommandId} was already recorded locally and was not executed again.")]
  private partial void LogDuplicateSuppressed(Guid commandId);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Interrupted image rollout command {CommandId} resolved as {Status}.")]
  private partial void LogInterruptedResolved(
      Guid commandId,
      string status);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Image rollout manifest reconstruction failed for command {CommandId}.")]
  private partial void LogManifestFailure(Guid commandId);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Image rollout command {CommandId} failed pre-handoff: {FailureCategory}.")]
  private partial void LogExecutionFailed(
      Guid commandId,
      string failureCategory);

  [LoggerMessage(
      Level = LogLevel.Error,
      Message = "The local image rollout ledger could not be accessed.")]
  private partial void LogLedgerFailure();
}
