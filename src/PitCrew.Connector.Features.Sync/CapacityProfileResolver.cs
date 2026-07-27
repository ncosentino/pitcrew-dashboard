using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

internal sealed record CapacityProfileDefinition(
    string ProfileId,
    int Generation,
    int CurrentMaximum,
    int MaximumAllowed,
    string Scope,
    string? RepositoryUrl,
    string Organization,
    string Enterprise,
    string Image,
    bool PullImage,
    IReadOnlyList<string> Labels,
    string NamePrefix,
    string RunnerGroup,
    bool Autoscale,
    int MinimumIdle,
    int ScaleDownDelaySeconds);

internal sealed record CapacityProfileResolution(
    CapacityProfileDefinition? Profile,
    string? Error);

internal sealed partial class CapacityProfileResolver(
    LocalProfileStateLocator _stateLocator,
    IOptions<ConnectorOptions> _options,
    ILogger<CapacityProfileResolver> _logger)
{
  private const int MaximumStateBytes = 1_048_576;

  public async Task<CapacityOperatorCapability?> ReadCapabilityAsync(
      CancellationToken cancellationToken)
  {
    if (!_options.Value.OperatorModeEnabled)
    {
      return null;
    }

    var profiles = new List<CapacityOperatorProfile>();
    foreach (var profileId in _options.Value.AllowedCapacityProfiles
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
      profiles.Add(new CapacityOperatorProfile(
          resolution.Profile.ProfileId,
          resolution.Profile.Generation,
          resolution.Profile.CurrentMaximum,
          resolution.Profile.MaximumAllowed));
    }
    return new CapacityOperatorCapability(profiles);
  }

  public async Task<CapacityProfileResolution> ResolveAsync(
      string profileId,
      CancellationToken cancellationToken)
  {
    if (!_options.Value.OperatorModeEnabled ||
        !_options.Value.AllowedCapacityProfiles.Contains(
            profileId,
            StringComparer.OrdinalIgnoreCase))
    {
      return new CapacityProfileResolution(
          null,
          "Profile is not enabled by local operator policy.");
    }

    var location = _stateLocator.Locate(profileId);
    if (location.Location is null)
    {
      return new CapacityProfileResolution(
          null,
          location.Error);
    }
    var profileDirectory = location.Location.ProfileDirectory;

    try
    {
      using var desired = JsonDocument.Parse(await LocalProfileStateLocator.ReadBoundedAsync(
          Path.Combine(profileDirectory, "desired-capacity.json"),
          MaximumStateBytes,
          cancellationToken));
      using var staticProfile = JsonDocument.Parse(await LocalProfileStateLocator.ReadBoundedAsync(
          Path.Combine(profileDirectory, "static-profile.json"),
          MaximumStateBytes,
          cancellationToken));
      return ParseProfile(
          profileId,
          desired.RootElement,
          staticProfile.RootElement);
    }
    catch (JsonException exception)
    {
      LogProfileReadFailure(profileId, exception.Message);
      return new CapacityProfileResolution(
          null,
          "Profile state is invalid.");
    }
    catch (InvalidDataException exception)
    {
      LogProfileReadFailure(profileId, exception.Message);
      return new CapacityProfileResolution(
          null,
          "Profile state is invalid.");
    }
    catch (IOException exception)
    {
      LogProfileReadFailure(profileId, exception.Message);
      return new CapacityProfileResolution(
          null,
          "Profile state could not be read.");
    }
    catch (UnauthorizedAccessException exception)
    {
      LogProfileReadFailure(profileId, exception.Message);
      return new CapacityProfileResolution(
          null,
          "Profile state could not be read.");
    }
    catch (KeyNotFoundException exception)
    {
      LogProfileReadFailure(profileId, exception.Message);
      return new CapacityProfileResolution(
          null,
          "Profile state is invalid.");
    }
    catch (InvalidOperationException exception)
    {
      LogProfileReadFailure(profileId, exception.Message);
      return new CapacityProfileResolution(
          null,
          "Profile state is invalid.");
    }
    catch (FormatException exception)
    {
      LogProfileReadFailure(profileId, exception.Message);
      return new CapacityProfileResolution(
          null,
          "Profile state is invalid.");
    }
  }

  private CapacityProfileResolution ParseProfile(
      string profileId,
      JsonElement desired,
      JsonElement staticProfile)
  {
    if (desired.GetProperty("schemaVersion").GetInt32() != 1 ||
        staticProfile.GetProperty("schemaVersion").GetInt32() != 1)
    {
      return new CapacityProfileResolution(
          null,
          "Profile state uses an unsupported schema.");
    }

    var generation = desired.GetProperty("generation").GetInt32();
    var scope = desired.GetProperty("scope").GetString();
    var configuration = staticProfile.GetProperty("configuration");
    if (generation < 1 ||
        scope is not ("repo" or "org" or "ent") ||
        !string.Equals(
            configuration.GetProperty("profile").GetString(),
            profileId,
            StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
            configuration.GetProperty("scope").GetString(),
            scope,
            StringComparison.Ordinal))
    {
      return new CapacityProfileResolution(
          null,
          "Desired and static profile state are inconsistent.");
    }

    string? repositoryUrl = null;
    int currentMaximum;
    var repositories = desired.GetProperty("repositories");
    if (scope == "repo")
    {
      if (repositories.GetArrayLength() != 1)
      {
        return new CapacityProfileResolution(
            null,
            "Repository profiles require exactly one existing target in this protocol version.");
      }
      var repository = repositories[0];
      repositoryUrl = repository.GetProperty("url").GetString();
      currentMaximum = repository.GetProperty("workers").GetInt32();
      if (string.IsNullOrWhiteSpace(repositoryUrl))
      {
        return new CapacityProfileResolution(
            null,
            "Repository capacity target is invalid.");
      }
    }
    else
    {
      currentMaximum = desired.GetProperty("replicas").GetInt32();
    }
    if (currentMaximum < 1)
    {
      return new CapacityProfileResolution(
          null,
          "Configured capacity maximum must be positive.");
    }

    var labels = new List<string>();
    using var labelEnumerator = configuration.GetProperty("labels")
        .EnumerateArray();
    while (labelEnumerator.MoveNext())
    {
      var label = labelEnumerator.Current.GetString();
      if (!string.IsNullOrWhiteSpace(label))
      {
        labels.Add(label);
      }
    }
    var autoscaling = configuration.GetProperty("autoscaling");
    var autoscale = autoscaling.ValueKind == JsonValueKind.Object;
    return new CapacityProfileResolution(
        new CapacityProfileDefinition(
            profileId,
            generation,
            currentMaximum,
            Math.Max(
                currentMaximum,
                _options.Value.CapacityMaximumCeiling),
            scope,
            repositoryUrl,
            configuration.GetProperty("organization").GetString() ??
                string.Empty,
            configuration.GetProperty("enterprise").GetString() ??
                string.Empty,
            configuration.GetProperty("image").GetString() ?? string.Empty,
            configuration.GetProperty("pullImage").GetBoolean(),
            labels,
            configuration.GetProperty("namePrefix").GetString() ??
                string.Empty,
            configuration.GetProperty("runnerGroup").GetString() ??
                string.Empty,
            autoscale,
            autoscale
                ? autoscaling.GetProperty("minimumIdle").GetInt32()
                : 0,
            autoscale
                ? autoscaling.GetProperty(
                    "scaleDownDelaySeconds").GetInt32()
                : 120),
        null);
  }

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Capacity operations are unavailable for profile {ProfileId}: {Reason}")]
  private partial void LogUnsupportedProfile(
      string profileId,
      string reason);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Capacity profile {ProfileId} could not be read: {Reason}")]
  private partial void LogProfileReadFailure(
      string profileId,
      string reason);
}
