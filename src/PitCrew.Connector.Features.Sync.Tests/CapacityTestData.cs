using System.Text.Json;

namespace PitCrew.Connector.Features.Sync.Tests;

internal static class CapacityTestData
{
  public static ConnectorOptions CreateOperatorOptions(
      string root,
      int ceiling = 50) =>
      new()
      {
        DashboardUrl = "https://dashboard.example",
        EnrollmentCode = "test-enrollment-code",
        DisplayName = "Test Server",
        StateRoot = Path.Combine(root, ".pitcrew-state"),
        IdentityPath = Path.Combine(root, "connector-identity.json"),
        OperatorModeEnabled = true,
        PitCrewRoot = root,
        AllowedCapacityProfiles = ["default"],
        CapacityMaximumCeiling = ceiling,
        CapacityCommandTimeoutSeconds = 60,
        ImageRolloutStatePath = Path.Combine(root, "image-rollout"),
        PowerShellExecutable = "pwsh",
      };

  public static async Task WriteSingleRepositoryProfileAsync(
      string root,
      int generation,
      int maximum,
      CancellationToken cancellationToken,
      int managerContractVersion = 17)
  {
    await File.WriteAllTextAsync(
        Path.Combine(root, "Setup-Runner.ps1"),
        "# test setup",
        cancellationToken);
    var profileDirectory = Directory.CreateDirectory(Path.Combine(
        root,
        ".pitcrew-state",
        "default"));
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory.FullName, "desired-capacity.json"),
        JsonSerializer.Serialize(new
        {
          schemaVersion = 1,
          generation,
          scope = "repo",
          repositories = new[]
          {
            new
            {
              url = "https://github.com/example/project",
              workers = maximum,
            },
          },
          replicas = (int?)null,
        }),
        cancellationToken);
    await File.WriteAllTextAsync(
        Path.Combine(profileDirectory.FullName, "static-profile.json"),
        JsonSerializer.Serialize(new
        {
          schemaVersion = 1,
          fingerprint = new string('a', 64),
          configuration = new
          {
            managerContractVersion,
            profile = "default",
            image = "example/runner:latest",
            pullImage = true,
            verificationCommands = Array.Empty<string>(),
            build = (object?)null,
            labels = new[] { "general-purpose" },
            disableDefaultLabels = false,
            scope = "repo",
            organization = string.Empty,
            enterprise = string.Empty,
            runnerGroup = string.Empty,
            autoscaling = new
            {
              mode = "scale-set",
              minimumIdle = 0,
              scaleDownDelaySeconds = 120,
            },
            namePrefix = "runner",
          },
        }),
        cancellationToken);
  }

  public static async Task WriteSecondRepositoryAsync(
      string root,
      CancellationToken cancellationToken)
  {
    var path = Path.Combine(
        root,
        ".pitcrew-state",
        "default",
        "desired-capacity.json");
    using var document = JsonDocument.Parse(
        await File.ReadAllTextAsync(path, cancellationToken));
    var generation = document.RootElement
        .GetProperty("generation")
        .GetInt32();
    await File.WriteAllTextAsync(
        path,
        JsonSerializer.Serialize(new
        {
          schemaVersion = 1,
          generation,
          scope = "repo",
          repositories = new[]
          {
            new
            {
              url = "https://github.com/example/project",
              workers = 10,
            },
            new
            {
              url = "https://github.com/example/second",
              workers = 5,
            },
          },
          replicas = (int?)null,
        }),
        cancellationToken);
  }

  /// <summary>
  /// Rewrites the current profile's static-profile.json to include a real
  /// <c>manifest = { kind, sourcePath, sha256, document }</c> object with a
  /// real regular file at <paramref name="manifestSourcePath"/> so
  /// <see cref="CapacityProfileResolver"/> accepts the path and the
  /// generated Setup-Runner invocation preserves it via -ProfilePath.
  /// </summary>
  public static async Task RewriteStaticProfileWithManifestAsync(
      string root,
      string manifestSourcePath,
      CancellationToken cancellationToken)
  {
    var staticPath = Path.Combine(
        root,
        ".pitcrew-state",
        "default",
        "static-profile.json");
    var text = await File.ReadAllTextAsync(staticPath, cancellationToken);
    using var document = JsonDocument.Parse(text);
    var configuration = document.RootElement
        .GetProperty("configuration");
    var fingerprint = document.RootElement
        .GetProperty("fingerprint").GetString();
    var configurationJson = configuration.GetRawText();
    using var configurationDocument = JsonDocument.Parse(configurationJson);
    // Write the manifest referenced file so IsValidLocalManifestPath passes
    // the "must exist" gate.
    Directory.CreateDirectory(
        Path.GetDirectoryName(manifestSourcePath)!);
    await File.WriteAllTextAsync(
        manifestSourcePath,
        JsonSerializer.Serialize(new
        {
          schemaVersion = 1,
          name = "default",
          description = "Local capacity manifest",
          replicas = 4,
          labels = new[] { "general-purpose" },
        }),
        cancellationToken);
    var manifestSha = Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(
            await File.ReadAllBytesAsync(manifestSourcePath, cancellationToken)));
    var manifest = new
    {
      kind = "external",
      sourcePath = manifestSourcePath,
      sha256 = manifestSha,
      document = JsonSerializer.Deserialize<Dictionary<string, object?>>(
          await File.ReadAllTextAsync(manifestSourcePath, cancellationToken)),
    };
    await File.WriteAllTextAsync(
        staticPath,
        JsonSerializer.Serialize(new
        {
          schemaVersion = 1,
          fingerprint,
          manifest,
          configuration =
              JsonSerializer.Deserialize<Dictionary<string, object?>>(
                  configurationJson),
        }),
        cancellationToken);
  }

  /// <summary>
  /// Rewrites the current profile's static-profile.json to include a
  /// <c>manifest</c> object with no <c>sourcePath</c> so the resolver fails
  /// closed instead of falling back to the repository's built-in profile.
  /// </summary>
  public static async Task RewriteStaticProfileWithManifestMissingSourcePathAsync(
      string root,
      CancellationToken cancellationToken)
  {
    var staticPath = Path.Combine(
        root,
        ".pitcrew-state",
        "default",
        "static-profile.json");
    var text = await File.ReadAllTextAsync(staticPath, cancellationToken);
    using var document = JsonDocument.Parse(text);
    var configurationJson = document.RootElement
        .GetProperty("configuration").GetRawText();
    var fingerprint = document.RootElement
        .GetProperty("fingerprint").GetString();
    await File.WriteAllTextAsync(
        staticPath,
        JsonSerializer.Serialize(new
        {
          schemaVersion = 1,
          fingerprint,
          manifest = new
          {
            kind = "external",
            // sourcePath deliberately absent
          },
          configuration =
              JsonSerializer.Deserialize<Dictionary<string, object?>>(
                  configurationJson),
        }),
        cancellationToken);
  }

  public static string CreateTemporaryDirectory()
  {
    var path = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-capacity-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
  }
}
