using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Resolves the locally authorized manager-recovery surface from PitCrew state.
/// </summary>
[DoNotAutoRegister]
internal sealed partial class RecoveryProfileResolver(
    LocalProfileStateLocator _stateLocator,
    LocalProfileOperationGate _operationGate,
    IHostExecutionEnvironment _executionEnvironment,
    IOptions<ConnectorOptions> _options,
    TimeProvider _timeProvider,
    ILogger<RecoveryProfileResolver> _logger)
{
  private const int MinimumManagerContractVersion = 9;

  /// <summary>
  /// Reads the recovery capability advertised to the dashboard.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels the read.</param>
  /// <returns>The capability, or <see langword="null"/> when recovery is locally disabled.</returns>
  public async Task<RecoveryOperatorCapability?> ReadCapabilityAsync(
      CancellationToken cancellationToken)
  {
    if (!IsLocallyEnabled())
    {
      return null;
    }

    var profiles = new List<RecoveryOperatorProfile>();
    foreach (var profileId in _options.Value.AllowedManagerRecoveryProfiles
        .Order(StringComparer.OrdinalIgnoreCase))
    {
      var resolution = await ResolveAsync(
          profileId,
          cancellationToken);
      if (resolution.Profile is null)
      {
        LogUnsupportedProfile(
            profileId,
            resolution.Error ?? "Unknown local profile error.");
        continue;
      }
      profiles.Add(new RecoveryOperatorProfile(
          resolution.Profile.ProfileId,
          resolution.Profile.ManagerContractVersion,
          resolution.Profile.ManagerContractSupported,
          resolution.Profile.ManagerInstanceId,
          resolution.Profile.Generation,
          resolution.Profile.DesiredStateHash,
          resolution.Profile.ObservedStateAgeSeconds,
          resolution.Profile.RecoveryAllowed,
          resolution.Profile.SingleManagerResolved,
          resolution.Profile.OperationActive,
          _options.Value.RecoveryCommandTimeoutSeconds,
          _options.Value.RecoveryCommandMaximumExpirySeconds));
    }
    return new RecoveryOperatorCapability(profiles);
  }

  /// <summary>
  /// Re-reads one profile's recovery state directly from local PitCrew state.
  /// </summary>
  /// <param name="profileId">Locally resolved profile identifier.</param>
  /// <param name="cancellationToken">Token that cancels the read.</param>
  /// <returns>The resolved state, or the local reason recovery is unavailable.</returns>
  public async Task<RecoveryProfileResolution> ResolveAsync(
      string profileId,
      CancellationToken cancellationToken)
  {
    if (!IsLocallyEnabled() ||
        !_options.Value.AllowedManagerRecoveryProfiles.Contains(
            profileId,
            StringComparer.OrdinalIgnoreCase))
    {
      return new RecoveryProfileResolution(
          null,
          "Profile is not enabled by local manager-recovery policy.",
          "not-allowed");
    }

    var location = _stateLocator.Locate(profileId);
    if (location.Location is null)
    {
      return new RecoveryProfileResolution(
          null,
          location.Error,
          "manager-unresolved");
    }
    if (File.Exists(Path.Combine(
        location.Location.ProfileDirectory,
        "manager-shutdown.json")))
    {
      return new RecoveryProfileResolution(
          null,
          "An explicit manager shutdown request is active.",
          "manager-unresolved");
    }

    ManagerObservedState? observed;
    try
    {
      var bytes = await LocalProfileStateLocator.ReadBoundedAsync(
          Path.Combine(
              location.Location.ProfileDirectory,
              "observed-state.json"),
          _options.Value.MaximumObservedStateBytes,
          cancellationToken);
      observed = JsonSerializer.Deserialize(
          bytes,
          PitCrewProtocolJsonContext.Default.ManagerObservedState);
    }
    catch (JsonException exception)
    {
      return UnreadableState(profileId, exception.Message);
    }
    catch (InvalidDataException exception)
    {
      return UnreadableState(profileId, exception.Message);
    }
    catch (IOException exception)
    {
      return UnreadableState(profileId, exception.Message);
    }
    catch (UnauthorizedAccessException exception)
    {
      return UnreadableState(profileId, exception.Message);
    }

    if (observed is null ||
        observed.SchemaVersion != 1 ||
        !string.Equals(
            observed.ProfileId,
            profileId,
            StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(observed.ManagerInstanceId) ||
        observed.ManagerInstanceId.Length > 128 ||
        observed.ManagerContractVersion < 1 ||
        observed.Generation < 0 ||
        observed.ObservedAt == default)
    {
      return new RecoveryProfileResolution(
          null,
          "Observed manager state is not coherent.",
          "manager-unresolved");
    }

    var ageSeconds = (int)Math.Max(
        0,
        Math.Round(
            (_timeProvider.GetUtcNow() - observed.ObservedAt).TotalSeconds));
    return new RecoveryProfileResolution(
        new RecoveryProfileState(
            profileId,
            observed.ManagerContractVersion,
            observed.ManagerContractVersion >= MinimumManagerContractVersion,
            observed.ManagerInstanceId,
            observed.Generation,
            observed.DesiredStateHash,
            ageSeconds,
            true,
            string.Equals(
                observed.ManagerStatus,
                "running",
                StringComparison.OrdinalIgnoreCase),
            _operationGate.IsActive(profileId)),
        null,
        null);
  }

  private bool IsLocallyEnabled() =>
      _options.Value.ManagerRecoveryEnabled &&
      !_executionEnvironment.IsContainer;

  private RecoveryProfileResolution UnreadableState(
      string profileId,
      string reason)
  {
    LogProfileReadFailure(profileId, reason);
    return new RecoveryProfileResolution(
        null,
        "Observed manager state could not be read.",
        "manager-unresolved");
  }

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Manager recovery is unavailable for profile {ProfileId}: {Reason}")]
  private partial void LogUnsupportedProfile(
      string profileId,
      string reason);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Observed manager state for profile {ProfileId} could not be read: {Reason}")]
  private partial void LogProfileReadFailure(
      string profileId,
      string reason);
}
