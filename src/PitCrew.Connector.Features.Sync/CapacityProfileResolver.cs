using System.Text.Json;
using System.Security.Cryptography;

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
    int ScaleDownDelaySeconds,
    bool SupportsZeroMaximum,
    string? ManifestSourcePath);

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
          resolution.Profile.MaximumAllowed,
          resolution.Profile.SupportsZeroMaximum));
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
    var supportsZeroMaximum =
        configuration.GetProperty("managerContractVersion").GetInt32() >= 17;
    if (currentMaximum < 0 ||
        (currentMaximum == 0 && !supportsZeroMaximum))
    {
      return new CapacityProfileResolution(
          null,
          "Configured capacity maximum is unsupported by this manager contract.");
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

    // A capacity-only invocation must never resolve the repository's
    // original build/image manifest after a rollout, which would silently
    // undo the rollout image authority. Behaviour depends on the applied
    // manifest kind:
    //
    //   * kind=external — the current state is a rollout-supplied manifest
    //     under the connector-controlled ImageRolloutStatePath\manifests
    //     directory. Validate the path via the shared state guard and
    //     forward it via -ProfilePath so Setup-Runner reuses it.
    //   * kind=built-in — the current state IS the untouched built-in
    //     profile.json under PitCrewRoot\profiles\<profileId>. Validate
    //     the path just enough to prove the state has not been rewritten
    //     to an arbitrary location, but do NOT forward it via
    //     -ProfilePath because Setup-Runner will find the built-in
    //     profile itself via its -Profile lookup.
    //   * manifest absent/null — treat as an implicit-default profile and
    //     omit -ProfilePath likewise.
    //
    // An anonymous manifest object with a missing/mismatched kind, missing
    // sourcePath, or an untrusted path still fails closed.
    string? manifestSourcePath = null;
    if (staticProfile.TryGetProperty("manifest", out var manifest) &&
        manifest.ValueKind == JsonValueKind.Object)
    {
      var manifestKind =
          manifest.TryGetProperty("kind", out var kind) &&
              kind.ValueKind == JsonValueKind.String
          ? kind.GetString()
          : null;
      var rawSourcePath = manifest.TryGetProperty("sourcePath", out var sp) &&
              sp.ValueKind == JsonValueKind.String
          ? sp.GetString()
          : null;
      var manifestSha256 =
          manifest.TryGetProperty("sha256", out var sha256) &&
              sha256.ValueKind == JsonValueKind.String
          ? sha256.GetString()
          : null;
      if (!string.IsNullOrWhiteSpace(rawSourcePath) &&
          !string.IsNullOrWhiteSpace(manifestKind) &&
          !string.IsNullOrWhiteSpace(manifestSha256))
      {
        if (!IsTrustedLocalManifestPath(
                profileId,
                manifestKind,
                rawSourcePath,
                manifestSha256))
        {
          return new CapacityProfileResolution(
              null,
              "Current static profile manifest source path is invalid or " +
              "not a locally trusted regular file.");
        }
        // Only forward the source path for external (rollout-supplied)
        // manifests. Built-in kind is validated but not forwarded.
        if (string.Equals(
                manifestKind,
                "external",
                StringComparison.Ordinal))
        {
          manifestSourcePath = rawSourcePath;
        }
      }
      else
      {
        // manifest is a non-empty object but has no sourcePath — the local
        // static state says an external manifest owns the profile but does
        // not expose where. Fail closed rather than fall back to the
        // repository's built-in profile.
        return new CapacityProfileResolution(
            null,
            "Current static profile records a manifest without a sourcePath; " +
            "cannot safely reconstruct capacity-only invocation.");
      }
    }
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
                : 120,
            supportsZeroMaximum,
            manifestSourcePath),
        null);
  }

  private bool IsTrustedLocalManifestPath(
      string profileId,
      string manifestKind,
      string rawSourcePath,
      string expectedSha256)
  {
    if (rawSourcePath.Length is < 1 or > 4096)
    {
      return false;
    }
    foreach (var character in rawSourcePath)
    {
      if (character is '\0' or '\r' or '\n' or '\t' or '"')
      {
        return false;
      }
    }
    string absolute;
    try
    {
      absolute = Path.GetFullPath(rawSourcePath);
    }
    catch (ArgumentException)
    {
      return false;
    }
    catch (PathTooLongException)
    {
      return false;
    }
    catch (NotSupportedException)
    {
      return false;
    }
    if (!Path.IsPathFullyQualified(absolute))
    {
      return false;
    }
    FileInfo info;
    try
    {
      info = new FileInfo(absolute);
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
    if (!info.Exists)
    {
      return false;
    }
    if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
        (info.Attributes & FileAttributes.Directory) != 0 ||
        info.Length is < 1 or > MaximumStateBytes)
    {
      return false;
    }
    if (!HasExpectedSha256(absolute, expectedSha256))
    {
      return false;
    }

    return manifestKind switch
    {
      "built-in" => IsTrustedBuiltInManifestPath(
          profileId,
          absolute),
      "external" => IsConnectorGeneratedManifestPath(absolute),
      _ => false,
    };
  }

  private bool IsTrustedBuiltInManifestPath(
      string profileId,
      string absolutePath)
  {
    try
    {
      var root = Path.GetFullPath(_options.Value.PitCrewRoot);
      var profilesDirectory = Path.Combine(root, "profiles");
      var profileDirectory = Path.Combine(profilesDirectory, profileId);
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(root);
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(profilesDirectory);
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(profileDirectory);
      var expectedPath = Path.GetFullPath(
          Path.Combine(profileDirectory, "profile.json"));
      return string.Equals(
          absolutePath,
          expectedPath,
          LocalPathComparison);
    }
    catch (ArgumentException)
    {
      return false;
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
  }

  private bool IsConnectorGeneratedManifestPath(string absolutePath)
  {
    try
    {
      var stateRoot = ImageRolloutStatePathGuard.CanonicalizeStateRoot(
          _options.Value.ImageRolloutStatePath);
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(stateRoot);
      var manifestDirectory =
          ImageRolloutStatePathGuard.CombineConfinedChild(
              stateRoot,
              "manifests");
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(manifestDirectory);
      if (!string.Equals(
              Path.GetDirectoryName(absolutePath),
              manifestDirectory,
              LocalPathComparison) ||
          !string.Equals(
              Path.GetExtension(absolutePath),
              ".json",
              StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }
      return Guid.TryParseExact(
          Path.GetFileNameWithoutExtension(absolutePath),
          "N",
          out _);
    }
    catch (ArgumentException)
    {
      return false;
    }
    catch (InvalidOperationException)
    {
      return false;
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
  }

  private static bool HasExpectedSha256(
      string path,
      string expectedSha256)
  {
    if (expectedSha256.Length != 64 ||
        expectedSha256.Any(character =>
            character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
    {
      return false;
    }
    try
    {
      using var stream = new FileStream(
          path,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read);
      var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
      return string.Equals(
          actual,
          expectedSha256,
          StringComparison.Ordinal);
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
  }

  private static StringComparison LocalPathComparison =>
      OperatingSystem.IsWindows()
          ? StringComparison.OrdinalIgnoreCase
          : StringComparison.Ordinal;

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
