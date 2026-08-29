using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Resolves the locally authorized profile-image rollout surface from PitCrew
/// static, desired, and observed state.
/// </summary>
/// <remarks>
/// <para>
/// This projection mirrors the exact upstream PitCrew executable contract:
/// <list type="bullet">
///   <item>Static profile root: <c>schemaVersion</c>, <c>fingerprint</c>,
///     <c>workerRevision</c>, optional <c>manifest = { kind, sourcePath, sha256, document }</c>,
///     and <c>configuration</c>. There is no root <c>localImageId</c>, no
///     <c>manifestDocument</c>, and the configuration has no
///     <c>imageDigest</c> or <c>architecture</c> property.</item>
///   <item>Desired capacity: <c>schemaVersion</c>, <c>generation</c>,
///     <c>scope</c>, <c>repositories</c>, <c>replicas</c>. There is no
///     <c>desiredStateHash</c>.</item>
///   <item>Observed state: <c>profileId</c>, <c>observedAt</c>, <c>scope</c>,
///     <c>generation</c>, <c>desiredStateHash</c>, <c>desiredStateStatus</c>,
///     <c>host.hardware.architecture</c> (canonical <c>amd64</c> /
///     <c>arm64</c>), and <c>update = { status, targetImage, targetImageId,
///     targetRevision, currentWorkers, staleWorkers, lastError }</c>. There
///     is no root <c>workers</c> object.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed partial class ImageRolloutProfileResolver(
    LocalProfileStateLocator _stateLocator,
    LocalProfileOperationGate _operationGate,
    ImageRolloutManifestBuilder _manifestBuilder,
    IHostExecutionEnvironment _executionEnvironment,
    IOptions<ConnectorOptions> _options,
    TimeProvider _timeProvider,
    ILogger<ImageRolloutProfileResolver> _logger)
{
  private const int MinimumManagerContractVersion = 17;
  private const int MaximumStateBytes = 1_048_576;

  // Bounded sentinel age emitted when observedAt is missing or unparseable.
  // Chosen equal to the wire's maximum accepted ObservedStateAgeSeconds so the
  // capability payload stays valid while every consumer freshness gate rejects
  // the profile (local executor cap 3600s; Dashboard capability freshness cap
  // is well below 86_400s).
  private const int StaleObservedAgeSentinel = 86_400;

  public async Task<ImageRolloutOperatorCapability?> ReadCapabilityAsync(
      CancellationToken cancellationToken)
  {
    if (!IsLocallyEnabled() ||
        !ValidateCapabilityPolicy(out var allowedRecipeIds))
    {
      return null;
    }

    var profiles = new List<ImageRolloutOperatorProfile>();
    foreach (var profileId in _options.Value.AllowedImageRolloutProfiles
        .Order(StringComparer.OrdinalIgnoreCase))
    {
      var resolution = await ResolveAsync(profileId, cancellationToken);
      if (resolution.Profile is null)
      {
        LogUnsupportedProfile(
            profileId,
            resolution.FailureCategory ?? "unknown");
        continue;
      }
      var profile = resolution.Profile;
      profiles.Add(new ImageRolloutOperatorProfile(
          profile.ProfileId,
          profile.Architecture,
          profile.CurrentImageReference,
          profile.CurrentImageDigest,
          profile.CurrentLocalImageId,
          profile.CurrentWorkerRevision,
          profile.StaticFingerprint,
          profile.PreservedConfigurationFingerprint,
          profile.RoutingFingerprint,
          profile.DesiredGeneration,
          profile.DesiredStateHash,
          allowedRecipeIds,
          profile.LocalFailureCategory is null &&
          profile.LocalSchemaSupported,
          profile.LocalSchemaSupported,
          profile.LocalFailureCategory,
          _operationGate.IsActive(profile.ProfileId),
          profile.ObservedStateAgeSeconds,
          _options.Value.ImageRolloutCommandTimeoutSeconds,
          _options.Value.ImageRolloutCommandMaximumExpirySeconds,
          profile.ManagerConvergenceStatus,
          profile.CurrentWorkers,
          profile.StaleWorkers));
    }
    return new ImageRolloutOperatorCapability(profiles);
  }

  public async Task<ImageRolloutProfileResolution> ResolveAsync(
      string profileId,
      CancellationToken cancellationToken)
  {
    if (!IsLocallyEnabled() ||
        !_options.Value.AllowedImageRolloutProfiles.Contains(
            profileId,
            StringComparer.OrdinalIgnoreCase))
    {
      return new ImageRolloutProfileResolution(
          null,
          "Profile is not enabled by local image rollout policy.",
          "not-allowed");
    }

    var location = _stateLocator.Locate(profileId);
    if (location.Location is null)
    {
      return new ImageRolloutProfileResolution(
          null,
          location.Error,
          "unsupported-schema");
    }

    string staticJson;
    string desiredJson;
    string observedJson;
    try
    {
      staticJson = await ReadAllAsync(
          Path.Combine(location.Location.ProfileDirectory, "static-profile.json"),
          cancellationToken);
      desiredJson = await ReadAllAsync(
          Path.Combine(location.Location.ProfileDirectory, "desired-capacity.json"),
          cancellationToken);
      observedJson = await ReadAllAsync(
          Path.Combine(location.Location.ProfileDirectory, "observed-state.json"),
          cancellationToken);
    }
    catch (JsonException)
    {
      return UnreadableState(profileId);
    }
    catch (InvalidDataException)
    {
      return UnreadableState(profileId);
    }
    catch (IOException)
    {
      return UnreadableState(profileId);
    }
    catch (UnauthorizedAccessException)
    {
      return UnreadableState(profileId);
    }

    return ProjectProfileState(
        profileId,
        staticJson,
        desiredJson,
        observedJson);
  }

  internal ImageRolloutProfileResolution ProjectProfileState(
      string profileId,
      string staticJson,
      string desiredJson,
      string observedJson)
  {
    try
    {
      using var staticProfile = JsonDocument.Parse(staticJson);
      using var desired = JsonDocument.Parse(desiredJson);
      using var observed = JsonDocument.Parse(observedJson);
      if (staticProfile.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
          desired.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
      {
        return new ImageRolloutProfileResolution(
            null,
            "Profile state uses an unsupported schema.",
            "unsupported-schema");
      }
      var configuration =
          staticProfile.RootElement.GetProperty("configuration");
      var contractVersion =
          configuration.GetProperty("managerContractVersion").GetInt32();

      // Architecture is authoritative: without a supported host architecture
      // no rollout can be safely operated. Return no capability row rather
      // than fabricating a `linux/amd64` placeholder that would misrepresent
      // an unsupported (or arm64) host on the wire. ReadCapabilityAsync
      // will log the closed category and skip the profile.
      var architecture = GetArchitectureOrNull(observed.RootElement);
      if (architecture is null)
      {
        return new ImageRolloutProfileResolution(
            null,
            "Host architecture is unavailable or unsupported.",
            "unsupported-architecture");
      }

      if (contractVersion < MinimumManagerContractVersion)
      {
        return new ImageRolloutProfileResolution(
            new ImageRolloutProfileState(
                profileId,
                architecture,
                CurrentImageReference: GetStringOrNull(configuration, "image"),
                CurrentImageDigest: ExtractDigestFromImageReference(
                    GetStringOrNull(configuration, "image")),
                CurrentLocalImageId: NormalizeDigest(
                    GetStringOrNull(configuration, "resolvedImageId")),
                CurrentWorkerRevision: GetStringOrNull(
                    staticProfile.RootElement,
                    "workerRevision"),
                StaticFingerprint: GetStringOrNull(
                    staticProfile.RootElement,
                    "fingerprint") ?? new string('0', 64),
                PreservedConfigurationFingerprint:
                    ComputePreservedConfigurationFingerprint(configuration),
                RoutingFingerprint: ComputeRoutingFingerprint(
                    desired.RootElement),
                DesiredGeneration: desired.RootElement
                    .GetProperty("generation").GetInt32(),
                DesiredStateHash: null,
                ObservedStateAgeSeconds: 0,
                ManagerConvergenceStatus: "degraded",
                CurrentWorkers: null,
                StaleWorkers: null,
                LocalSchemaSupported: false,
                LocalFailureCategory: "unsupported-manager",
                StaticProfileJson: staticJson,
                ManifestSourcePath: GetManifestSourcePathOrNull(
                    staticProfile.RootElement),
                NamePrefix: GetStringOrNull(configuration, "namePrefix"),
                Routing: ProjectRouting(desired.RootElement, configuration)),
            null,
            null);
      }

      if (!string.Equals(
          configuration.GetProperty("profile").GetString(),
          profileId,
          StringComparison.OrdinalIgnoreCase))
      {
        return new ImageRolloutProfileResolution(
            null,
            "Profile state does not describe the expected profile.",
            "unsupported-schema");
      }

      var currentImage = GetStringOrNull(configuration, "image");
      var currentDigest = ExtractDigestFromImageReference(currentImage);
      var rawCurrentLocalImageId = GetStringOrNull(
          configuration,
          "resolvedImageId");
      var currentLocalImageId = NormalizeDigest(rawCurrentLocalImageId);
      if (rawCurrentLocalImageId is not null &&
          currentLocalImageId is null)
      {
        return new ImageRolloutProfileResolution(
            null,
            "Static profile state has an invalid resolved image id.",
            "unsupported-schema");
      }
      var currentWorkerRevision = NormalizeHexOrNull(GetStringOrNull(
          staticProfile.RootElement,
          "workerRevision"));
      var staticFingerprintValue = NormalizeHexOrNull(GetStringOrNull(
          staticProfile.RootElement,
          "fingerprint"));
      if (currentWorkerRevision is null ||
          staticFingerprintValue is null)
      {
        return new ImageRolloutProfileResolution(
            null,
            "Static profile state has invalid revision authority.",
            "unsupported-schema");
      }

      var preservedConfigurationFingerprint =
          ComputePreservedConfigurationFingerprint(configuration);
      var routingFingerprint = ComputeRoutingFingerprint(desired.RootElement);
      var generation = desired.RootElement.GetProperty("generation").GetInt32();
      var routingState = ProjectRouting(desired.RootElement, configuration);
      var manifestSourcePath = GetManifestSourcePathOrNull(
          staticProfile.RootElement);
      _manifestBuilder.ValidateReconstructable(profileId, staticJson);

      // Desired generation/hash come exclusively from the observed state
      // (which acknowledges what the manager has actually accepted). We
      // require the observation to match the local desired profileId/scope/
      // generation and desiredStateStatus to be an accepted acknowledgement.
      var observedProjection = ProjectObservedState(
          observed.RootElement,
          desired.RootElement,
          profileId,
          contractVersion,
          currentWorkerRevision,
          currentImage,
          currentLocalImageId);

      return new ImageRolloutProfileResolution(
          new ImageRolloutProfileState(
              profileId,
              architecture,
              currentImage,
              currentDigest,
              currentLocalImageId,
              currentWorkerRevision,
              staticFingerprintValue!,
              preservedConfigurationFingerprint,
              routingFingerprint,
              // Only publish an acknowledged generation matching local desired.
              observedProjection.AcknowledgedGeneration ?? generation,
              observedProjection.DesiredStateHash,
              observedProjection.AgeSeconds,
              observedProjection.Convergence,
              observedProjection.CurrentWorkers,
              observedProjection.StaleWorkers,
              LocalSchemaSupported: true,
              LocalFailureCategory: observedProjection.FailureCategory,
              staticJson,
              manifestSourcePath,
              GetStringOrNull(configuration, "namePrefix"),
              routingState),
          null,
          null);
    }
    catch (JsonException)
    {
      LogProjectionFailure(profileId);
      return new ImageRolloutProfileResolution(
          null,
          "Profile state is invalid.",
          "unsupported-schema");
    }
    catch (KeyNotFoundException)
    {
      LogProjectionFailure(profileId);
      return new ImageRolloutProfileResolution(
          null,
          "Profile state is invalid.",
          "unsupported-schema");
    }
    catch (InvalidOperationException)
    {
      LogProjectionFailure(profileId);
      return new ImageRolloutProfileResolution(
          null,
          "Profile state is invalid.",
          "unsupported-schema");
    }
    catch (FormatException)
    {
      LogProjectionFailure(profileId);
      return new ImageRolloutProfileResolution(
          null,
          "Profile state is invalid.",
          "unsupported-schema");
    }
    catch (InvalidTopologyDataException)
    {
      // ProjectRouting fails closed via InvalidTopologyDataException when
      // the desired document contains a malformed repository entry, an
      // unsupported scope, missing routing identity, or otherwise violates
      // the local routing contract. Skip the profile with the distinct
      // closed unsupported-topology category so operators can tell a
      // topology failure apart from a schema/manager rejection.
      LogProjectionFailure(profileId);
      return new ImageRolloutProfileResolution(
          null,
          "Profile state is invalid.",
          "unsupported-topology");
    }
    catch (InvalidDataException)
    {
      // ProjectObservedState (and other schema-level projections) fail
      // closed via generic InvalidDataException for structural/manager
      // violations. Skip the profile with the closed unsupported-schema
      // category rather than propagating.
      LogProjectionFailure(profileId);
      return new ImageRolloutProfileResolution(
          null,
          "Profile state is invalid.",
          "unsupported-schema");
    }
  }

  /// <summary>
  /// Attempts to project a canonical linux/&lt;arch&gt; identifier from the
  /// observed host hardware architecture ("amd64"→"linux/amd64",
  /// "arm64"→"linux/arm64"). Returns <see langword="null"/> when absent or
  /// unsupported so the caller fails closed with unsupported-architecture.
  /// </summary>
  private static string? GetArchitectureOrNull(
      JsonElement observed)
  {
    if (!observed.TryGetProperty("host", out var host) ||
        host.ValueKind != JsonValueKind.Object ||
        !host.TryGetProperty("hardware", out var hardware) ||
        hardware.ValueKind != JsonValueKind.Object ||
        !hardware.TryGetProperty("architecture", out var raw) ||
        raw.ValueKind != JsonValueKind.String)
    {
      return null;
    }
    var value = raw.GetString();
    if (string.Equals(value, "amd64", StringComparison.Ordinal))
    {
      return "linux/amd64";
    }
    if (string.Equals(value, "arm64", StringComparison.Ordinal))
    {
      return "linux/arm64";
    }
    return null;
  }

  /// <summary>
  /// Projects an <see cref="ImageRolloutRoutingState"/> from the desired
  /// document and the currently applied configuration. Fails closed with an
  /// <see cref="InvalidTopologyDataException"/> when the desired document is
  /// malformed, contains a scope the connector cannot preserve, or is
  /// missing required routing identity/counts. Callers catch this and
  /// classify the profile as <c>unsupported-topology</c>, which is
  /// distinct from schema/manager failures and preserved on the wire.
  /// </summary>
  private static ImageRolloutRoutingState ProjectRouting(
      JsonElement desired,
      JsonElement configuration)
  {
    var scope = GetStringOrNull(desired, "scope");
    var organization = GetStringOrNull(configuration, "organization")
        ?? string.Empty;
    var enterprise = GetStringOrNull(configuration, "enterprise")
        ?? string.Empty;
    switch (scope)
    {
      case "repo":
        return ProjectRepoRouting(desired);
      case "org":
        return ProjectOrgOrEntRouting(
            desired,
            scope,
            organization,
            enterprise: string.Empty,
            requiredIdentity: organization,
            identityName: "organization");
      case "ent":
        return ProjectOrgOrEntRouting(
            desired,
            scope,
            organization: string.Empty,
            enterprise,
            requiredIdentity: enterprise,
            identityName: "enterprise");
      default:
        throw new InvalidTopologyDataException(
            "Desired routing scope is missing or unsupported.");
    }
  }

  private static ImageRolloutRoutingState ProjectRepoRouting(
      JsonElement desired)
  {
    if (!desired.TryGetProperty("repositories", out var repositories) ||
        repositories.ValueKind != JsonValueKind.Array)
    {
      throw new InvalidTopologyDataException(
          "Desired repositories must be an array for repo scope.");
    }
    // Repository URLs are case-insensitive on GitHub. Track duplicates
    // case-insensitively so a rewritten-case entry cannot smuggle in a
    // duplicate target.
    var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var targets = new List<ImageRolloutRepositoryTarget>();
    using (var entryEnumerator = repositories.EnumerateArray())
    {
      while (entryEnumerator.MoveNext())
      {
        var entry = entryEnumerator.Current;
        if (entry.ValueKind != JsonValueKind.Object)
        {
          throw new InvalidTopologyDataException(
              "Desired repositories entry is not an object.");
        }
        var url = GetStringOrNull(entry, "url");
        if (string.IsNullOrWhiteSpace(url) ||
            !IsCanonicalRepositoryUrl(url!))
        {
          throw new InvalidTopologyDataException(
              "Desired repositories entry has a missing or noncanonical url.");
        }
        if (!seenUrls.Add(url!))
        {
          throw new InvalidTopologyDataException(
              "Desired repositories entry is a duplicate.");
        }
        if (!entry.TryGetProperty("workers", out var workersElement) ||
            workersElement.ValueKind != JsonValueKind.Number ||
            !workersElement.TryGetInt32(out var workers) ||
            workers < 0)
        {
          throw new InvalidTopologyDataException(
              "Desired repositories entry has an invalid workers count.");
        }
        // Protocol v11 supports only a single repo target. Include the
        // entry regardless of count so BuildArguments can enforce a
        // Count==1 invariant; the single-target check is asserted below,
        // paused vs active is derived from Workers.
        targets.Add(new ImageRolloutRepositoryTarget(url!, workers));
      }
    }
    if (targets.Count == 0)
    {
      // repo scope must have at least one repository entry (paused or not).
      throw new InvalidTopologyDataException(
          "Desired repo routing requires at least one repository entry.");
    }
    if (targets.Count > 1)
    {
      // PowerShell -File binding rejects a repeated `-AddRepos` switch
      // ("parameter specified more than once", exit 1), binds only the
      // first adjacent value to a [string[]] parameter, and treats a
      // comma-joined value as a single string. There is therefore no
      // safe way to project more than one repository target through the
      // Setup-Runner CLI in protocol v11, so multi-target routing fails
      // closed at this boundary. Callers surface this as the closed
      // `unsupported-topology` category so operators can distinguish it
      // from schema or manager failures. This mirrors the existing
      // capacity protocol's single-target invariant (see
      // `CapacityProfileDefinition.RepositoryUrl`).
      throw new InvalidTopologyDataException(
          "Desired repo routing has more than one repository entry; " +
          "protocol v11 supports only a single repository target.");
    }
    return new ImageRolloutRoutingState(
        Scope: "repo",
        Paused: targets[0].Workers == 0,
        RepositoryTargets: targets,
        Organization: string.Empty,
        Enterprise: string.Empty,
        Replicas: null);
  }

  private static ImageRolloutRoutingState ProjectOrgOrEntRouting(
      JsonElement desired,
      string scope,
      string organization,
      string enterprise,
      string requiredIdentity,
      string identityName)
  {
    if (string.IsNullOrWhiteSpace(requiredIdentity))
    {
      throw new InvalidTopologyDataException(
          $"Local {identityName} identity is required for {scope} scope.");
    }
    if (desired.TryGetProperty("repositories", out var repositories) &&
        repositories.ValueKind == JsonValueKind.Array &&
        repositories.GetArrayLength() > 0)
    {
      throw new InvalidTopologyDataException(
          $"Desired routing for {scope} scope must not contain repositories.");
    }
    if (!desired.TryGetProperty("replicas", out var replicasElement) ||
        replicasElement.ValueKind == JsonValueKind.Null)
    {
      throw new InvalidTopologyDataException(
          $"Desired routing for {scope} scope must include replicas.");
    }
    if (replicasElement.ValueKind != JsonValueKind.Number ||
        !replicasElement.TryGetInt32(out var replicas) ||
        replicas < 0)
    {
      throw new InvalidTopologyDataException(
          $"Desired routing for {scope} scope has an invalid replicas value.");
    }
    return new ImageRolloutRoutingState(
        Scope: scope,
        Paused: replicas == 0,
        RepositoryTargets: Array.Empty<ImageRolloutRepositoryTarget>(),
        Organization: organization,
        Enterprise: enterprise,
        Replicas: replicas);
  }

  private static bool IsCanonicalRepositoryUrl(string url)
  {
    // Canonical: https://<host>/<owner>/<repo>; disallow whitespace,
    // control characters, credentials, and empty path components.
    if (!url.StartsWith("https://", StringComparison.Ordinal))
    {
      return false;
    }
    foreach (var character in url)
    {
      if (char.IsControl(character) || char.IsWhiteSpace(character))
      {
        return false;
      }
    }
    if (url.Contains('@', StringComparison.Ordinal))
    {
      return false;
    }
    var afterScheme = url["https://".Length..];
    var parts = afterScheme.Split('/', StringSplitOptions.None);
    return parts.Length >= 3 &&
        !string.IsNullOrEmpty(parts[0]) &&
        !string.IsNullOrEmpty(parts[1]) &&
        !string.IsNullOrEmpty(parts[2]);
  }

  /// <summary>
  /// Returns the connector-generated manifest source path recorded on the
  /// current static profile, or <see langword="null"/> if the current static
  /// profile has no locally applied manifest.
  /// </summary>
  private static string? GetManifestSourcePathOrNull(JsonElement staticProfile)
  {
    if (!staticProfile.TryGetProperty("manifest", out var manifest) ||
        manifest.ValueKind != JsonValueKind.Object)
    {
      return null;
    }
    return GetStringOrNull(manifest, "sourcePath");
  }

  /// <summary>
  /// Extracts a lowercase <c>sha256:&lt;hex&gt;</c> digest from a digest-
  /// qualified image reference (<c>repo@sha256:...</c>). Returns
  /// <see langword="null"/> for tag-only references. Upstream does not
  /// expose a distinct <c>imageDigest</c> field in configuration.
  /// </summary>
  private static string? ExtractDigestFromImageReference(string? reference)
  {
    if (string.IsNullOrEmpty(reference))
    {
      return null;
    }
    var separatorIndex = reference.IndexOf('@');
    if (separatorIndex < 0 || separatorIndex >= reference.Length - 1)
    {
      return null;
    }
    return NormalizeDigest(reference[(separatorIndex + 1)..]);
  }

  private static string ComputePreservedConfigurationFingerprint(
      JsonElement configuration)
  {
    // Fingerprint every configuration property except the intentionally
    // changed image authority fields (image, resolvedImageId, pullImage,
    // build). Do not maintain a hand-written allowlist; upstream can add
    // new configuration fields (e.g. workerRuntimeContractVersion,
    // disableDefaultLabels, readOnlyVolumes, hostAdmission) and every one
    // must be preserved by rollout.
    var canonical = new SortedDictionary<string, object?>(
        StringComparer.Ordinal);
    using var enumerator = configuration.EnumerateObject();
    while (enumerator.MoveNext())
    {
      var name = enumerator.Current.Name;
      if (string.Equals(name, "image", StringComparison.Ordinal) ||
          string.Equals(name, "resolvedImageId", StringComparison.Ordinal) ||
          string.Equals(name, "pullImage", StringComparison.Ordinal) ||
          string.Equals(name, "build", StringComparison.Ordinal))
      {
        continue;
      }
      canonical[name] = ExtractValue(enumerator.Current.Value);
    }
    return Sha256Hex(canonical);
  }

  private static string ComputeRoutingFingerprint(JsonElement desired)
  {
    // Fingerprint every desired-capacity property except schemaVersion and
    // generation (generation is separately fenced by the wire). This covers
    // scope, repositories (with URLs and counts), and replicas without
    // relying on upstream fields that do not exist here (organization,
    // enterprise, pause, desiredStateHash).
    var canonical = new SortedDictionary<string, object?>(
        StringComparer.Ordinal);
    using var enumerator = desired.EnumerateObject();
    while (enumerator.MoveNext())
    {
      var name = enumerator.Current.Name;
      if (string.Equals(name, "schemaVersion", StringComparison.Ordinal) ||
          string.Equals(name, "generation", StringComparison.Ordinal))
      {
        continue;
      }
      canonical[name] = ExtractValue(enumerator.Current.Value);
    }
    return Sha256Hex(canonical);
  }

  private static object? ExtractValue(JsonElement element)
  {
    switch (element.ValueKind)
    {
      case JsonValueKind.String:
        return element.GetString();
      case JsonValueKind.Number:
        return element.GetRawText();
      case JsonValueKind.True:
        return true;
      case JsonValueKind.False:
        return false;
      case JsonValueKind.Null:
        return null;
      case JsonValueKind.Array:
        {
          var items = new List<object?>();
          using var enumerator = element.EnumerateArray();
          while (enumerator.MoveNext())
          {
            items.Add(ExtractValue(enumerator.Current));
          }
          return items.ToArray();
        }
      case JsonValueKind.Object:
        {
          var map = new SortedDictionary<string, object?>(
              StringComparer.Ordinal);
          using var enumerator = element.EnumerateObject();
          while (enumerator.MoveNext())
          {
            map[enumerator.Current.Name] =
                ExtractValue(enumerator.Current.Value);
          }
          return map;
        }
      default:
        return null;
    }
  }

  private static string Sha256Hex(
      SortedDictionary<string, object?> canonical)
  {
    var json = JsonSerializer.SerializeToUtf8Bytes(
        canonical,
        new JsonSerializerOptions
        {
          Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
          WriteIndented = false,
        });
    var hash = SHA256.HashData(json);
    return Convert.ToHexStringLower(hash);
  }

  private static string? GetStringOrNull(JsonElement source, string name)
  {
    return source.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.String
        ? element.GetString()
        : null;
  }

  private static string? NormalizeDigest(string? value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return null;
    }
    var trimmed = value.Trim();
    if (trimmed.StartsWith("sha256:", StringComparison.Ordinal) &&
        trimmed.Length == 71)
    {
      var hex = trimmed.AsSpan(7);
      foreach (var character in hex)
      {
        if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
        {
          return null;
        }
      }
      return trimmed;
    }
    return null;
  }

  private ObservedProjection ProjectObservedState(
      JsonElement observed,
      JsonElement desired,
      string profileId,
      int staticManagerContractVersion,
      string? currentWorkerRevision,
      string? currentImage,
      string? currentLocalImageId)
  {
    var observedFailureCategory = (string?)null;

    // Observed managerContractVersion must exist and equal the applied
    // static configuration's managerContractVersion; otherwise the
    // observation was produced by a manager the current static state
    // does not reflect and any acknowledged desired-state hash, update
    // target, or convergence counts are unauthoritative.
    if (!observed.TryGetProperty(
            "managerContractVersion",
            out var observedContractElement) ||
        observedContractElement.ValueKind != JsonValueKind.Number ||
        !observedContractElement.TryGetInt32(out var observedContractVersion) ||
        observedContractVersion != staticManagerContractVersion)
    {
      observedFailureCategory = "stale-observed-state";
    }

    DateTimeOffset? observedAt = null;
    if (observed.TryGetProperty("observedAt", out var observedAtElement) &&
        observedAtElement.ValueKind == JsonValueKind.String)
    {
      if (DateTimeOffset.TryParse(
          observedAtElement.GetString(),
          System.Globalization.CultureInfo.InvariantCulture,
          System.Globalization.DateTimeStyles.AssumeUniversal |
              System.Globalization.DateTimeStyles.AdjustToUniversal,
          out var parsed))
      {
        observedAt = parsed;
      }
      else
      {
        observedFailureCategory = "stale-observed-state";
      }
    }
    else
    {
      observedFailureCategory = "stale-observed-state";
    }
    var age = observedAt is null
        ? StaleObservedAgeSentinel
        : (int)Math.Max(
            0,
            Math.Min(
                StaleObservedAgeSentinel,
                Math.Round((_timeProvider.GetUtcNow() - observedAt.Value)
                    .TotalSeconds)));

    // Match the local desired document. If the observed acknowledgement
    // does not match the local profile/scope/generation, the acknowledged
    // desired-state hash is not authoritative and the profile must be
    // treated as stale-observed-state (not guessed).
    var observedProfileId = GetStringOrNull(observed, "profileId");
    var observedScope = GetStringOrNull(observed, "scope");
    var desiredScope = GetStringOrNull(desired, "scope");
    int? observedGeneration = null;
    if (observed.TryGetProperty("generation", out var observedGenerationElement) &&
        observedGenerationElement.ValueKind == JsonValueKind.Number &&
        observedGenerationElement.TryGetInt32(out var parsedObservedGen))
    {
      observedGeneration = parsedObservedGen;
    }
    var desiredGeneration = desired.GetProperty("generation").GetInt32();
    var desiredStateStatus = GetStringOrNull(observed, "desiredStateStatus");
    var isDesiredMatch =
        string.Equals(observedProfileId, profileId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(observedScope, desiredScope, StringComparison.Ordinal) &&
        observedGeneration == desiredGeneration &&
        desiredStateStatus is "accepted";

    string? desiredStateHash = null;
    if (isDesiredMatch)
    {
      desiredStateHash = NormalizeHexOrNull(
          GetStringOrNull(observed, "desiredStateHash"));
      if (desiredStateHash is null)
      {
        observedFailureCategory ??= "stale-observed-state";
      }
    }
    else
    {
      observedFailureCategory ??= "stale-observed-state";
    }

    // update is required. Missing update makes the observation
    // unauthoritative for every rollout gate.
    if (!observed.TryGetProperty("update", out var update) ||
        update.ValueKind != JsonValueKind.Object)
    {
      return new ObservedProjection(
          age,
          "degraded",
          CurrentWorkers: null,
          StaleWorkers: null,
          FailureCategory: observedFailureCategory
              ?? "stale-observed-state",
          AcknowledgedGeneration: null,
          DesiredStateHash: null);
    }

    // Validate status; unknown status is stale, not degraded fallback.
    var status = GetStringOrNull(update, "status");
    if (status is not ("current" or "rolling" or "degraded"))
    {
      observedFailureCategory ??= "stale-observed-state";
    }

    // Counts must be present non-negative integers; missing/negative
    // counts make the observation stale.
    int? current = null;
    int? stale = null;
    if (update.TryGetProperty("currentWorkers", out var currentElement) &&
        currentElement.ValueKind == JsonValueKind.Number &&
        currentElement.TryGetInt32(out var parsedCurrent) &&
        parsedCurrent >= 0)
    {
      current = parsedCurrent;
    }
    else
    {
      observedFailureCategory ??= "stale-observed-state";
    }
    if (update.TryGetProperty("staleWorkers", out var staleElement) &&
        staleElement.ValueKind == JsonValueKind.Number &&
        staleElement.TryGetInt32(out var parsedStale) &&
        parsedStale >= 0)
    {
      stale = parsedStale;
    }
    else
    {
      observedFailureCategory ??= "stale-observed-state";
    }

    string? lastError = null;
    if (update.TryGetProperty("lastError", out var lastErrorElement))
    {
      if (lastErrorElement.ValueKind == JsonValueKind.String &&
          !string.IsNullOrWhiteSpace(lastErrorElement.GetString()))
      {
        lastError = lastErrorElement.GetString();
      }
      else if (lastErrorElement.ValueKind != JsonValueKind.Null)
      {
        observedFailureCategory ??= "stale-observed-state";
      }
    }
    else
    {
      observedFailureCategory ??= "stale-observed-state";
    }
    var statusAndCountsAgree = status switch
    {
      "current" => stale == 0 && lastError is null,
      "rolling" => stale > 0 && lastError is null,
      "degraded" => lastError is not null,
      _ => false,
    };
    if (!statusAndCountsAgree)
    {
      observedFailureCategory ??= "stale-observed-state";
    }

    // The update target identity describes the static target for every
    // status (current, rolling, degraded). Enforce presence and
    // consistency with configuration for all statuses, not only current.
    var targetImage = GetStringOrNull(update, "targetImage");
    var targetImageId = NormalizeDigest(GetStringOrNull(update, "targetImageId"));
    var targetRevision = GetStringOrNull(update, "targetRevision");
    if (string.IsNullOrWhiteSpace(targetImage) ||
        string.IsNullOrWhiteSpace(targetRevision))
    {
      observedFailureCategory ??= "stale-observed-state";
    }
    if (!IsUpdateConsistentWithCurrent(
            targetImage,
            targetImageId,
            targetRevision,
            currentImage,
            currentLocalImageId,
            currentWorkerRevision))
    {
      observedFailureCategory ??= "stale-observed-state";
    }

    var convergence = status switch
    {
      "current" => "current",
      "rolling" => "rolling",
      _ => "degraded",
    };
    return new ObservedProjection(
        age,
        convergence,
        current,
        stale,
        observedFailureCategory,
        isDesiredMatch ? observedGeneration : null,
        desiredStateHash);
  }

  private static bool IsUpdateConsistentWithCurrent(
      string? targetImage,
      string? targetImageId,
      string? targetRevision,
      string? currentImage,
      string? currentLocalImageId,
      string? currentWorkerRevision)
  {
    // The observed update target identity describes the static target for
    // every reported status. Any contradiction with the currently applied
    // configuration means the observation no longer describes local state
    // and must be treated as stale.
    if (!string.Equals(
            targetImage,
            currentImage,
            StringComparison.Ordinal))
    {
      return false;
    }
    if (!string.Equals(
            targetImageId,
            currentLocalImageId,
            StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }
    if (!string.Equals(
            targetRevision,
            currentWorkerRevision,
            StringComparison.Ordinal))
    {
      return false;
    }
    return true;
  }

  private static string? NormalizeHexOrNull(string? value)
  {
    if (string.IsNullOrEmpty(value) || value.Length != 64)
    {
      return null;
    }
    foreach (var character in value)
    {
      if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
      {
        return null;
      }
    }
    return value;
  }

  private async Task<string> ReadAllAsync(
      string path,
      CancellationToken cancellationToken)
  {
    var bytes = await LocalProfileStateLocator.ReadBoundedAsync(
        path,
        MaximumStateBytes,
        cancellationToken);
    return Encoding.UTF8.GetString(bytes);
  }

  private bool IsLocallyEnabled() =>
      _options.Value.ImageRolloutEnabled &&
      !_executionEnvironment.IsContainer;

  private bool ValidateCapabilityPolicy(
      out IReadOnlyList<string> allowedRecipeIds)
  {
    allowedRecipeIds = [];
    var options = _options.Value;
    if (options.ImageRolloutCommandTimeoutSeconds is < 60 or > 3600 ||
        options.ImageRolloutCommandMaximumExpirySeconds is < 60 or > 86400 ||
        options.ImageRolloutObservedStateMaximumAgeSeconds is < 30 or > 3600 ||
        options.AllowedImageRolloutProfiles.Length == 0 ||
        options.ImageRolloutRecipes.Count == 0)
    {
      LogInvalidPolicy();
      return false;
    }

    var seenProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var profileId in options.AllowedImageRolloutProfiles)
    {
      if (!PitCrewProfileId.IsValid(profileId) ||
          !seenProfiles.Add(profileId))
      {
        LogInvalidPolicy();
        return false;
      }
    }

    var recipeIds = new List<string>();
    var seenRecipeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var recipe in options.ImageRolloutRecipes)
    {
      if (recipe is null ||
          !ImageRolloutRecipePolicy.IsValidRecipeId(recipe.RecipeId) ||
          !ImageRolloutRecipePolicy.IsValidRegistryRepository(
              recipe.RegistryRepository) ||
          !seenRecipeIds.Add(recipe.RecipeId))
      {
        LogInvalidPolicy();
        return false;
      }
      recipeIds.Add(recipe.RecipeId);
    }

    try
    {
      var stateRoot = ImageRolloutStatePathGuard.CanonicalizeStateRoot(
          options.ImageRolloutStatePath);
      if (!Directory.Exists(stateRoot))
      {
        LogInvalidPolicy();
        return false;
      }
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(stateRoot);
      var manifestDirectory =
          ImageRolloutStatePathGuard.CombineConfinedChild(
              stateRoot,
              "manifests");
      ImageRolloutStatePathGuard.EnsureNotReparsePoint(manifestDirectory);
    }
    catch (ArgumentException)
    {
      LogInvalidPolicy();
      return false;
    }
    catch (InvalidOperationException)
    {
      LogInvalidPolicy();
      return false;
    }
    catch (IOException)
    {
      LogInvalidPolicy();
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      LogInvalidPolicy();
      return false;
    }

    allowedRecipeIds = recipeIds
        .OrderBy(recipeId => recipeId, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    return true;
  }

  private ImageRolloutProfileResolution UnreadableState(
      string profileId)
  {
    LogProfileReadFailure(profileId);
    return new ImageRolloutProfileResolution(
        null,
        "Local profile state could not be read.",
        "unsupported-schema");
  }

  private readonly record struct ObservedProjection(
      int AgeSeconds,
      string Convergence,
      int? CurrentWorkers,
      int? StaleWorkers,
      string? FailureCategory,
      int? AcknowledgedGeneration,
      string? DesiredStateHash);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Image rollout is unavailable for profile {ProfileId}: {FailureCategory}.")]
  private partial void LogUnsupportedProfile(
      string profileId,
      string failureCategory);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Image rollout capability is unavailable because local rollout policy is invalid or its protected state directory is unavailable.")]
  private partial void LogInvalidPolicy();

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Image rollout profile {ProfileId} state could not be read.")]
  private partial void LogProfileReadFailure(
      string profileId);

  [LoggerMessage(
      Level = LogLevel.Warning,
      Message = "Image rollout profile {ProfileId} state could not be projected.")]
  private partial void LogProjectionFailure(
      string profileId);

  /// <summary>
  /// Marks routing-projection failures (malformed repositories, unsupported
  /// scope, missing required identity/counts) so <c>ProjectProfileState</c>
  /// can classify them as the distinct <c>unsupported-topology</c> closed
  /// category rather than being collapsed into <c>unsupported-schema</c>.
  /// Derives from <see cref="Exception"/> directly because
  /// <see cref="InvalidDataException"/> is sealed in the current runtime.
  /// </summary>
  private sealed class InvalidTopologyDataException : Exception
  {
    public InvalidTopologyDataException()
    {
    }

    public InvalidTopologyDataException(string message)
        : base(message)
    {
    }

    public InvalidTopologyDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
  }
}
