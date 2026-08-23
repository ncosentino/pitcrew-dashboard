using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Microsoft.Extensions.DependencyInjection;

using PitCrew.Support.Canary.Contracts;
using PitCrew.Support.Canary.Scenarios;

namespace PitCrew.Support.Canary.Tests;

[NotInParallel]
public sealed class CanaryTopologyTests
{
  [Test]
  public async Task Aspire_And_External_Runtime_Use_The_Same_Smoke_Scenario(
      CancellationToken cancellationToken)
  {
    var dcpAvailable = IsDcpAvailable();
    if (string.Equals(
            Environment.GetEnvironmentVariable(
                "PITCREW_CANARY_REQUIRE_ASPIRE_TESTING"),
            "true",
            StringComparison.OrdinalIgnoreCase) &&
        !dcpAvailable)
    {
      throw new InvalidOperationException(
          "The required Aspire DCP executable is unavailable.");
    }
    Skip.Unless(
        dcpAvailable,
        "Aspire DCP is installed by the dedicated support-canary workflow.");
    var repositoryRoot = FindRepositoryRoot();
    var runRoot = Path.Combine(
        Path.GetTempPath(),
        "pitcrew-support-canary-tests",
        $"canary-topology-{Guid.NewGuid():N}");
    Directory.CreateDirectory(runRoot);
    var runId = Guid.NewGuid().ToString("N");
    var commit = new string('a', 40);
    var plan = new CanaryPlanManifest(
        CanaryManifestFile.PlanSchemaVersion,
        runId,
        CanaryTopologyProfiles.Portable,
        ["topology-smoke-v1"],
        new CanarySourceRevision(
            "ncosentino/pitcrew-dashboard",
            commit),
        new CanarySourceRevision(
            "ncosentino/pitcrew",
            commit),
        DateTimeOffset.UtcNow);
    CanaryManifestFile.WritePlan(
        Path.Combine(runRoot, "plan.json"),
        plan);
    using var environment = new EnvironmentVariableScope(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
          ["PITCREW_CANARY_RUN_ROOT"] = runRoot,
          ["PITCREW_CANARY_RUN_ID"] = runId,
          ["PITCREW_CANARY_DASHBOARD_SOURCE_ROOT"] = repositoryRoot,
          ["PITCREW_CANARY_DOTNET_CONFIGURATION"] =
              GetBuildConfiguration(),
          ["Parameters__relay-secret"] =
              "canary-test-relay-secret-not-for-production",
          ["Parameters__dashboard-authorization-key"] =
              CreateDashboardAuthorizationKey(),
          ["Parameters__dashboard-result-key"] =
              CreateDashboardResultKey(),
        });
    DistributedApplication? application = null;
    try
    {
      var appHost =
          await DistributedApplicationTestingBuilder.CreateAsync<
              Projects.PitCrew_Support_Canary_AppHost>()
              .WaitAsync(
                  TimeSpan.FromSeconds(60),
                  cancellationToken);
      application = await appHost.BuildAsync(cancellationToken)
          .WaitAsync(
              TimeSpan.FromSeconds(60),
              cancellationToken);
      await application.StartAsync(cancellationToken)
          .WaitAsync(
              TimeSpan.FromSeconds(60),
              cancellationToken);
      var notifications = application.Services
          .GetRequiredService<ResourceNotificationService>();
      await notifications.WaitForResourceHealthyAsync(
              "dashboard",
              cancellationToken)
          .WaitAsync(
              TimeSpan.FromSeconds(60),
              cancellationToken);
      await notifications.WaitForResourceHealthyAsync(
              "support-relay",
              cancellationToken)
          .WaitAsync(
              TimeSpan.FromSeconds(60),
              cancellationToken);
      var runtimePath = Path.Combine(
          runRoot,
          "runtime.json");
      await WaitForFileAsync(
          runtimePath,
          cancellationToken);
      var runtime = CanaryManifestFile.ReadRuntime(runtimePath);
      var scenario = CanaryScenarioRegistry.ResolveOrNull(
          "topology-smoke-v1") ??
          throw new InvalidOperationException(
              "The topology smoke scenario is not registered.");

      var result = await scenario.RunAsync(
          runtime,
          new CanaryScenarioContext(
              runRoot,
              repositoryRoot,
              repositoryRoot,
              TimeProvider.System),
          cancellationToken);

      await Assert.That(result.Status)
          .IsEqualTo("succeeded");
      await Assert.That(result.Steps)
          .Count()
          .IsEqualTo(2);
      await Assert.That(result.Steps.Select(step => step.Name))
          .IsEquivalentTo(
          [
              "dashboard-health",
              "relay-health",
          ]);
    }
    finally
    {
      try
      {
        if (application is not null)
        {
          await File.WriteAllTextAsync(
              Path.Combine(
                  runRoot,
                  "stop.request"),
              runId,
              cancellationToken);
          using var stopTimeout =
              CancellationTokenSource.CreateLinkedTokenSource(
                  cancellationToken);
          stopTimeout.CancelAfter(TimeSpan.FromSeconds(30));
          await application.StopAsync(stopTimeout.Token)
              .WaitAsync(
                  TimeSpan.FromSeconds(30),
                  cancellationToken);
          await application.DisposeAsync()
              .AsTask()
              .WaitAsync(
                  TimeSpan.FromSeconds(30),
                  cancellationToken);
        }
      }
      finally
      {
        Directory.Delete(runRoot, recursive: true);
      }
    }
  }

  private static bool IsDcpAvailable()
  {
    var configuredPath = Environment.GetEnvironmentVariable(
        "ASPIRE_DCP_PATH");
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
      return File.Exists(configuredPath);
    }
    var executable = OperatingSystem.IsWindows()
        ? "dcp.exe"
        : "dcp";
    return File.Exists(
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            ".aspire",
            "bundle",
            "dcp",
            executable));
  }

  private static string GetBuildConfiguration()
  {
    var releaseSegment =
        $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}";
    return AppContext.BaseDirectory.Contains(
        releaseSegment,
        StringComparison.OrdinalIgnoreCase)
        ? "Release"
        : "Debug";
  }

  private static string CreateDashboardAuthorizationKey()
  {
    using var key = System.Security.Cryptography.ECDsa.Create(
        System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
    return ToBase64Url(key.ExportPkcs8PrivateKey());
  }

  private static string CreateDashboardResultKey()
  {
    using var key = System.Security.Cryptography.RSA.Create(3072);
    return ToBase64Url(key.ExportPkcs8PrivateKey());
  }

  private static string ToBase64Url(byte[] value) =>
      Convert.ToBase64String(value)
          .TrimEnd('=')
          .Replace('+', '-')
          .Replace('/', '_');

  private static async Task WaitForFileAsync(
      string path,
      CancellationToken cancellationToken)
  {
    using var timer = new PeriodicTimer(
        TimeSpan.FromMilliseconds(100));
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(60));
    while (await timer.WaitForNextTickAsync(timeout.Token))
    {
      if (File.Exists(path))
      {
        return;
      }
    }
    throw new InvalidOperationException(
        "The runtime manifest was not emitted.");
  }

  private static string FindRepositoryRoot()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
      if (File.Exists(
          Path.Combine(
              directory.FullName,
              "PitCrew.Dashboard.slnx")))
      {
        return directory.FullName;
      }
      directory = directory.Parent;
    }
    throw new InvalidOperationException(
        "The repository root was not found.");
  }
}
