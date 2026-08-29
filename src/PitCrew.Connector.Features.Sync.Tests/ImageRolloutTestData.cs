using System.Text.Json;

namespace PitCrew.Connector.Features.Sync.Tests;

/// <summary>
/// Deterministic offline fixtures that mirror the exact upstream PitCrew
/// executable state:
/// <list type="bullet">
///   <item>Static profile root: <c>schemaVersion, fingerprint, workerRevision,
///     manifest = { kind, sourcePath, sha256, document }, configuration</c>.
///     There is no root <c>localImageId</c>, no <c>manifestDocument</c>, and
///     configuration has no <c>imageDigest</c> or <c>architecture</c>.</item>
///   <item>Configuration byte-count properties are integers
///     (<c>memoryBytes</c>, <c>memorySwapBytes</c>, <c>sharedMemoryBytes</c>);
///     <c>cpuCores</c> is a string (e.g. <c>"2"</c>); <c>pids</c> is an
///     integer; the runtime carries <c>devices</c> (closed literal set
///     <c>kvm</c>, not <c>runtimeClass</c> and not a raw device path);
///     <c>serviceNetwork</c> is an object <c>{ source }</c>;
///     <c>readOnlyVolumes</c> is an array of <c>{ name, source, target }</c>
///     objects where <c>name</c> and <c>source</c> are Docker volume names
///     matching the profile pattern (target is computed and stripped
///     during reconstruction).</item>
///   <item>manifest.document is a strict subset of
///     <c>runner-profile.schema.json</c>: it contains <c>schemaVersion,
///     name, description, image, labels (required), replicas (integer
///     &gt;=1), pullImage, hostAdmission</c> (with capacityUnits/
///     safetyMarginUnits/workerCostUnits/reservationUnits/borrowable, no
///     <c>identity</c>), and an optional <c>build = { context, dockerfile
///     }</c>. It does not contain <c>profile</c>,
///     <c>managerContractVersion</c>, <c>scope</c>, <c>namePrefix</c>, or
///     <c>imageDigest</c>.</item>
///   <item>Desired capacity: <c>schemaVersion, generation, scope,
///     repositories, replicas</c>. No <c>desiredStateHash</c>.</item>
///   <item>Observed state: <c>profileId, observedAt, scope, generation,
///     desiredStateHash, desiredStateStatus, host.hardware.architecture</c>
///     (canonical <c>amd64</c>/<c>arm64</c>), and
///     <c>update = { status, targetImage, targetImageId, targetRevision,
///     currentWorkers, staleWorkers, lastError }</c>. No root
///     <c>workers</c>.</item>
/// </list>
/// </summary>
internal static partial class ImageRolloutTestData
{
  public const string DefaultProfileId = "default";
  public const string DefaultRecipeId = "copilot-cli";
  public const string DefaultRegistryRepository = "ghcr.io/example/runner";
  public const string TargetDigest =
      "sha256:0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
  public const string CurrentImageDigest =
      "sha256:1111111111111111111111111111111111111111111111111111111111111111";
  public const string CurrentLocalImageId =
      "sha256:2222222222222222222222222222222222222222222222222222222222222222";
  public const string DefaultRepositoryUrl =
      "https://github.com/example/project";
  public const int DefaultRepositoryWorkers = 4;
  public const string DefaultHostAdmissionNamespace = "pitcrew-runner-ns";

  public static readonly DateTimeOffset Now = new(
      2026,
      8,
      1,
      12,
      0,
      0,
      TimeSpan.Zero);

  public static string StaticFingerprint { get; } = new('a', 64);
  public static string CurrentWorkerRevision { get; } = new('d', 64);
  public static string DesiredStateHash { get; } = new('e', 64);

  public static ConnectorOptions CreateOperatorOptions(string root)
  {
    var options = CapacityTestData.CreateOperatorOptions(root);
    options.ImageRolloutEnabled = true;
    options.AllowedImageRolloutProfiles = [DefaultProfileId];
    options.ImageRolloutRecipes =
        new List<ImageRolloutRecipePolicyEntry>
        {
          new()
          {
            RecipeId = DefaultRecipeId,
            RegistryRepository = DefaultRegistryRepository,
          },
        };
    options.ImageRolloutStatePath = Path.Combine(root, "image-rollout");
    options.ImageRolloutCommandTimeoutSeconds = 600;
    options.ImageRolloutCommandMaximumExpirySeconds = 1_800;
    options.ImageRolloutObservedStateMaximumAgeSeconds = 300;
    options.ImageRolloutRetainedManifests = 4;
    // Mirror the installer contract: the connector fails closed unless the
    // rollout state root already exists with restrictive ownership. Tests
    // pre-provision the directory so exercised code paths behave identically
    // to a production install.
    Directory.CreateDirectory(options.ImageRolloutStatePath);
    return options;
  }

  public static async Task WriteHealthyStateAsync(
      string root,
      CancellationToken cancellationToken,
      string profileId = DefaultProfileId,
      int managerContractVersion = 17,
      string architectureLabel = "linux-amd64",
      string architecture = "amd64",
      string currentImage = "ghcr.io/example/runner:main",
      bool includeManifest = true,
      DivergentManifestDocumentValues? divergentManifestDocument = null)
  {
    await File.WriteAllTextAsync(
        Path.Combine(root, "Setup-Runner.ps1"),
        "# test setup",
        cancellationToken);
    var profileDirectory = Directory.CreateDirectory(
        Path.Combine(root, ".pitcrew-state", profileId));

    // The upstream runner-profile.schema.json is
    // additionalProperties=false. The manifest document carries the
    // schema's descriptive/policy fields (name/description/image/labels/
    // replicas/pullImage/hostAdmission/build). It MUST NOT contain
    // profile, managerContractVersion, scope, namePrefix, imageDigest,
    // resolvedImageId, or a string serviceNetwork.
    //
    // Divergent values are optional overrides so tests can prove the
    // manifest reconstruction uses the applied configuration values
    // instead of any stale source manifest values.
    var manifestLabels = divergentManifestDocument?.Labels ?? new[]
    {
      "general-purpose",
      architectureLabel,
    };
    var manifestVerificationCommands =
        divergentManifestDocument?.VerificationCommands
        ?? (object)new[] { "which docker" };

    var manifestDescription = divergentManifestDocument?.Description
        ?? "Local test runner profile";
    var manifestReplicas = divergentManifestDocument?.Replicas
        ?? DefaultRepositoryWorkers;
    var manifestDocument = new Dictionary<string, object?>
    {
      ["schemaVersion"] = 1,
      ["name"] = profileId,
      ["description"] = manifestDescription,
      ["image"] = currentImage,
      ["labels"] = manifestLabels,
      ["replicas"] = manifestReplicas,
      ["pullImage"] = true,
      ["hostAdmission"] = new
      {
        // Full hostAdmission policy per upstream schema. There is no
        // "identity" property. capacityUnits/safetyMarginUnits/
        // workerCostUnits/reservationUnits/borrowable are the actual
        // upstream shape.
        @namespace = DefaultHostAdmissionNamespace,
        capacityUnits = 128,
        safetyMarginUnits = 16,
        workerCostUnits = 1,
        reservationUnits = 4,
        borrowable = true,
      },
      ["verificationCommands"] = manifestVerificationCommands,
      ["build"] = new
      {
        // Upstream uses "dockerfile", not "file".
        context = "./",
        dockerfile = "Dockerfile",
      },
    };
    var manifestSourcePath = Path.Combine(
        profileDirectory.FullName,
        "runner-profile.json");
    // Persist a real manifest file so Capacity tests can validate its
    // existence-and-regular-file gate.
    await File.WriteAllTextAsync(
        manifestSourcePath,
        JsonSerializer.Serialize(manifestDocument),
        cancellationToken);
    object? manifest = includeManifest
        ? new
        {
          kind = "external",
          sourcePath = manifestSourcePath,
          sha256 = new string('7', 64),
          document = manifestDocument,
        }
        : null;

    // Applied static configuration includes CLI overrides so labels/
    // runnerGroup/autoscaling/resources/runtime/readOnlyVolumes/
    // serviceNetwork/verificationCommands live here, not on the source
    // manifest document. Divergent overrides in the manifest document are
    // provably discarded during reconstruction.
    var configurationLabels = new[]
    {
      "general-purpose",
      architectureLabel,
    };
    var configurationRunnerGroup = "";
    var configurationAutoscaling = new
    {
      mode = "scale-set",
      minimumIdle = 0,
      scaleDownDelaySeconds = 120,
      maximumActiveWorkers = (int?)null,
    };
    var configurationResources = new
    {
      // cpuCores is a STRING in the applied configuration (e.g. "2").
      cpuCores = "1",
      memoryBytes = 2L * 1024L * 1024L * 1024L,
      memorySwapBytes = 4L * 1024L * 1024L * 1024L,
      pids = 4096,
    };
    var configurationRuntime = new
    {
      sharedMemoryBytes = 64L * 1024L * 1024L,
      // runner-profile.schema.json restricts runtime.devices to a closed
      // literal set (kvm). The raw device path "/dev/kvm" is a Setup-Runner
      // implementation detail and is schema-invalid on the manifest.
      devices = new[] { "kvm" },
    };
    var configurationReadOnlyVolumes = new object[]
    {
      new
      {
        // name and source must satisfy runner-profile.schema.json's Docker
        // volume-name pattern: the source is a named Docker volume, never
        // a filesystem path with '/', whitespace, or control characters.
        name = "runner-cache",
        source = "runner-cache-src",
        // target is a Setup-Runner-computed mount under /mnt/pitcrew-data
        // and is stripped during reconstruction. The applied static state
        // carries the target upstream produces so tests exercise the
        // "computed target dropped" branch faithfully.
        target = "/mnt/pitcrew-data/runner-cache",
      },
    };
    var configurationServiceNetwork = new
    {
      source = "runner-net",
    };
    var configurationVerificationCommands = new[] { "which docker" };

    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory.FullName, "static-profile.json"),
        JsonSerializer.Serialize(new
        {
          schemaVersion = 1,
          fingerprint = StaticFingerprint,
          workerRevision = CurrentWorkerRevision,
          manifest,
          configuration = new
          {
            managerContractVersion,
            workerRuntimeContractVersion = 5,
            profile = profileId,
            image = currentImage,
            resolvedImageId = CurrentLocalImageId,
            pullImage = true,
            verificationCommands = configurationVerificationCommands,
            build = (object?)new
            {
              context = "./",
              dockerfile = "Dockerfile",
            },
            labels = configurationLabels,
            disableDefaultLabels = false,
            scope = "repo",
            organization = "",
            enterprise = "",
            runnerGroup = configurationRunnerGroup,
            autoscaling = configurationAutoscaling,
            hostAdmission = new
            {
              // Only the namespace lives in applied configuration; the
              // full policy is preserved from manifest.document.
              @namespace = DefaultHostAdmissionNamespace,
            },
            resources = configurationResources,
            runtime = configurationRuntime,
            readOnlyVolumes = configurationReadOnlyVolumes,
            serviceNetwork = configurationServiceNetwork,
            namePrefix = "runner",
          },
        }),
        cancellationToken);

    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory.FullName, "desired-capacity.json"),
        JsonSerializer.Serialize(new
        {
          schemaVersion = 1,
          generation = 7,
          scope = "repo",
          repositories = new[]
          {
            new
            {
              url = DefaultRepositoryUrl,
              workers = DefaultRepositoryWorkers,
            },
          },
          replicas = (int?)null,
        }),
        cancellationToken);

    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory.FullName, "observed-state.json"),
        JsonSerializer.Serialize(new
        {
          schemaVersion = 1,
          // Observed managerContractVersion must equal the static
          // configuration's managerContractVersion for the observation to
          // be accepted as authoritative.
          managerContractVersion,
          profileId,
          observedAt = Now,
          scope = "repo",
          generation = 7,
          desiredStateHash = DesiredStateHash,
          desiredStateStatus = "accepted",
          host = new
          {
            hardware = new
            {
              architecture,
            },
          },
          update = new
          {
            status = "current",
            targetImage = currentImage,
            targetImageId = CurrentLocalImageId,
            targetRevision = CurrentWorkerRevision,
            currentWorkers = DefaultRepositoryWorkers,
            staleWorkers = 0,
            lastError = (string?)null,
          },
        }),
        cancellationToken);
  }

  /// <summary>
  /// Overwrites desired-capacity.json with a repositories entry that
  /// violates canonical shape (duplicate URL, negative worker count, or
  /// malformed URL) so tests can prove <c>ProjectRouting</c> fails
  /// closed rather than silently skipping malformed entries.
  /// </summary>
  public static async Task WriteDesiredWithMalformedRepositoriesAsync(
      string root,
      CancellationToken cancellationToken,
      string profileId = DefaultProfileId)
  {
    var profileDirectory = Path.Combine(root, ".pitcrew-state", profileId);
    Directory.CreateDirectory(profileDirectory);
    var payload = new
    {
      schemaVersion = 1,
      generation = 7,
      scope = "repo",
      // Malformed: negative worker count. Must fail closed.
      repositories = new object[]
      {
        new
        {
          url = "https://github.com/example/runner",
          workers = -1,
        },
      },
    };
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory, "desired-capacity.json"),
        JsonSerializer.Serialize(payload),
        cancellationToken);
  }

  /// <summary>
  /// Overwrites desired-capacity.json with an unsupported <c>scope</c>
  /// so tests can prove <c>ProjectRouting</c> fails closed rather than
  /// coercing unknown routing into paused/empty state.
  /// </summary>
  public static async Task WriteDesiredWithUnsupportedScopeAsync(
      string root,
      CancellationToken cancellationToken,
      string profileId = DefaultProfileId)
  {
    var profileDirectory = Path.Combine(root, ".pitcrew-state", profileId);
    Directory.CreateDirectory(profileDirectory);
    var payload = new
    {
      schemaVersion = 1,
      generation = 7,
      scope = "cluster",
      repositories = Array.Empty<object>(),
      replicas = 1,
    };
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory, "desired-capacity.json"),
        JsonSerializer.Serialize(payload),
        cancellationToken);
  }

  /// <summary>
  /// Validates that the produced runner-profile manifest matches a focused
  /// subset of the upstream <c>runner-profile.schema.json</c>. The bounded
  /// check lives in <see cref="ImageRolloutManifestSchemaAssertions"/>; this
  /// method preserves the call surface used by existing tests.
  /// </summary>
  public static Task AssertProducedManifestConformsToSchemaAsync(
      JsonElement manifest,
      CancellationToken cancellationToken) =>
      ImageRolloutManifestSchemaAssertions.AssertConformsAsync(
          manifest,
          cancellationToken);
}
