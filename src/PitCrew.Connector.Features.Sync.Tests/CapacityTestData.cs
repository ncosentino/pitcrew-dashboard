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

  public static string CreateTemporaryDirectory()
  {
    var path = Path.Combine(
        Path.GetTempPath(),
        $"pitcrew-capacity-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
  }
}
