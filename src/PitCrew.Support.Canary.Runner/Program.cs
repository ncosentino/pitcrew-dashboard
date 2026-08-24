using System.Globalization;

using PitCrew.Support.Canary.Contracts;
using PitCrew.Support.Canary.Runner;
using PitCrew.Support.Canary.Scenarios;

#pragma warning disable NLF0001 // This executable's stdout/stderr are its bounded CLI contract.
return await CanaryRunnerProgram.RunAsync(
    args,
    CancellationToken.None);

internal static class CanaryRunnerProgram
{
  public static async Task<int> RunAsync(
      string[] args,
      CancellationToken cancellationToken)
  {
    if (args.Length == 0)
    {
      return Fail("command-required");
    }
    try
    {
      return args[0] switch
      {
        "emit-runtime" => EmitRuntime(args[1..]),
        "run" => await RunScenarioAsync(
            args[1..],
            cancellationToken),
        "inject-rejected-requests" =>
            await RunRejectedRequestInjectorAsync(
                args[1..],
                cancellationToken),
        "list" => ListScenarios(),
        _ => Fail("command-unsupported"),
      };
    }
    catch (InvalidDataException)
    {
      return Fail("invalid-manifest");
    }
    catch (IOException)
    {
      return Fail("filesystem-unavailable");
    }
    catch (UnauthorizedAccessException)
    {
      return Fail("filesystem-forbidden");
    }
  }

  private static async Task<int>
      RunRejectedRequestInjectorAsync(
          string[] args,
          CancellationToken cancellationToken)
  {
    var runRoot = ReadRequiredArgument(args, "--run-root");
    var plan = CanaryManifestFile.ReadPlan(
        Path.Combine(runRoot, "plan.json"));
    var relayInternalUrl = new Uri(
        EnsureOrigin(
            ReadRequiredEnvironment(
                "PITCREW_CANARY_RELAY_INTERNAL_URL")),
        UriKind.Absolute);
    if (!relayInternalUrl.IsLoopback ||
        relayInternalUrl.Scheme != Uri.UriSchemeHttp)
    {
      throw new InvalidDataException(
          "The rejected-request injector relay origin is invalid.");
    }
    await CanaryRejectedRequestInjector.RunAsync(
        Path.GetFullPath(runRoot),
        plan.RunId,
        relayInternalUrl,
        ReadRequiredEnvironment(
            "PITCREW_CANARY_RELAY_SECRET"),
        ReadRequiredEnvironment(
            "PITCREW_CANARY_DASHBOARD_AUTHORIZATION_KEY"),
        TimeProvider.System,
        cancellationToken);
    return 0;
  }

  private static int EmitRuntime(string[] args)
  {
    var runRoot = ReadRequiredArgument(args, "--run-root");
    var plan = CanaryManifestFile.ReadPlan(
        Path.Combine(runRoot, "plan.json"));
    var dashboardUrl = ReadRequiredEnvironment(
        "PITCREW_CANARY_DASHBOARD_URL");
    var relayUrl = ReadRequiredEnvironment(
        "PITCREW_CANARY_RELAY_URL");
    var runtime = new CanaryRuntimeManifest(
        CanaryManifestFile.RuntimeSchemaVersion,
        plan.RunId,
        plan.TopologyProfile,
        plan.Dashboard,
        plan.PitCrew,
        EnsureOrigin(dashboardUrl),
        EnsureOrigin(relayUrl),
        GetCapabilities(plan.TopologyProfile),
        DateTimeOffset.UtcNow);
    CanaryManifestFile.WriteRuntime(
        Path.Combine(runRoot, "runtime.json"),
        runtime);
    return 0;
  }

  private static IReadOnlyList<string> GetCapabilities(
      string topologyProfile) =>
      topologyProfile switch
      {
        CanaryTopologyProfiles.Portable =>
        [
            CanaryCapabilities.DashboardHttp,
            CanaryCapabilities.RelayHttp,
            CanaryCapabilities.SupportAgentProcess,
            CanaryCapabilities.SupportBrokerProcess,
            CanaryCapabilities.PitCrewFileOnlyEvidence,
            CanaryCapabilities.RelayRestartControl,
            CanaryCapabilities.RejectedRequestInjection,
        ],
        CanaryTopologyProfiles.Containerized =>
        [
            CanaryCapabilities.DashboardHttp,
            CanaryCapabilities.RelayHttp,
            CanaryCapabilities.SupportAgentProcess,
            CanaryCapabilities.SupportBrokerProcess,
            CanaryCapabilities.PitCrewFileOnlyEvidence,
            CanaryCapabilities.CandidateContainerImages,
            CanaryCapabilities.ContainerSessionNetwork,
            CanaryCapabilities.ContainerRunScopedStorage,
            CanaryCapabilities.RelayRestartControl,
            CanaryCapabilities.RejectedRequestInjection,
        ],
        CanaryTopologyProfiles.WindowsInstalled =>
        [
            CanaryCapabilities.DashboardHttp,
            CanaryCapabilities.RelayHttp,
            CanaryCapabilities.PitCrewFileOnlyEvidence,
            CanaryCapabilities.WindowsInstalledServices,
            CanaryCapabilities.WindowsServiceIsolation,
            CanaryCapabilities.RelayRestartControl,
        ],
        CanaryTopologyProfiles.LinuxInstalled =>
        [
            CanaryCapabilities.DashboardHttp,
            CanaryCapabilities.RelayHttp,
            CanaryCapabilities.PitCrewFileOnlyEvidence,
            CanaryCapabilities.LinuxInstalledServices,
            CanaryCapabilities.LinuxSystemdIsolation,
            CanaryCapabilities.RelayRestartControl,
        ],
        _ => throw new InvalidDataException(
            "The canary topology profile is unsupported."),
      };

  private static async Task<int> RunScenarioAsync(
      string[] args,
      CancellationToken cancellationToken)
  {
    var runRoot = ReadRequiredArgument(args, "--run-root");
    var scenarioId = ReadRequiredArgument(args, "--scenario");
    var dashboardSourceRoot = ReadRequiredArgument(
        args,
        "--dashboard-source-root");
    var pitCrewSourceRoot = ReadRequiredArgument(
        args,
        "--pitcrew-source-root");
    var timeoutSeconds = ReadBoundedIntegerArgument(
        args,
        "--timeout-seconds",
        30,
        1800,
        300);
    var runtime = CanaryManifestFile.ReadRuntime(
        Path.Combine(runRoot, "runtime.json"));
    var plan = CanaryManifestFile.ReadPlan(
        Path.Combine(runRoot, "plan.json"));
    if (!plan.Scenarios.Contains(
            scenarioId,
            StringComparer.Ordinal))
    {
      return Fail("scenario-not-selected");
    }
    var scenario = CanaryScenarioRegistry.ResolveOrNull(scenarioId);
    if (scenario is null)
    {
      return Fail("scenario-unsupported");
    }
    if (!scenario.RequiredCapabilities.IsSubsetOf(
            runtime.Capabilities))
    {
      return Fail("topology-capability-missing");
    }
    var context = new CanaryScenarioContext(
        Path.GetFullPath(runRoot),
        Path.GetFullPath(dashboardSourceRoot),
        Path.GetFullPath(pitCrewSourceRoot),
        TimeProvider.System);
    return await ExecuteScenarioAsync(
        scenario,
        runtime,
        context,
        TimeSpan.FromSeconds(timeoutSeconds),
        cancellationToken);
  }

  internal static async Task<int> ExecuteScenarioAsync(
      ICanaryScenario scenario,
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      TimeSpan timeout,
      CancellationToken cancellationToken)
  {
    var result = await CanaryScenarioExecutor.RunAsync(
        scenario,
        runtime,
        context,
        timeout,
        cancellationToken);
    var runRoot = context.RunRoot;
    var evidenceRoot = Path.Combine(runRoot, "evidence");
    Directory.CreateDirectory(evidenceRoot);
    var resultPath = Path.Combine(
        evidenceRoot,
        $"{scenario.Id}.json");
    try
    {
      CanaryManifestFile.WriteScenarioResult(
          resultPath,
          result);
    }
    catch (InvalidDataException)
    {
      var now = TimeProvider.System.GetUtcNow();
      result = new CanaryScenarioResult(
          CanaryManifestFile.ScenarioResultSchemaVersion,
          runtime.RunId,
          scenario.Id,
          runtime.TopologyProfile,
          "failed",
          "scenario-result-invalid",
          [
              new CanaryScenarioStepResult(
                  "scenario-execution",
                  "failed",
                  "scenario-result-invalid",
                  0),
          ],
          now,
          now);
      CanaryManifestFile.WriteScenarioResult(
          resultPath,
          result);
    }
    return result.Status == "succeeded"
        ? 0
        : Fail(result.FailureCategory ?? "scenario-failed");
  }

  private static int ListScenarios()
  {
    foreach (var scenarioId in CanaryScenarioRegistry.ScenarioIds
                 .Order(StringComparer.Ordinal))
    {
      Console.WriteLine(scenarioId);
    }
    return 0;
  }

  private static string ReadRequiredArgument(
      string[] args,
      string name)
  {
    var index = Array.IndexOf(args, name);
    if (index < 0 ||
        index == args.Length - 1 ||
        string.IsNullOrWhiteSpace(args[index + 1]))
    {
      throw new InvalidDataException(
          "A required canary argument is missing.");
    }
    return args[index + 1];
  }

  private static int ReadBoundedIntegerArgument(
      string[] args,
      string name,
      int minimum,
      int maximum,
      int defaultValue)
  {
    var index = Array.IndexOf(args, name);
    if (index < 0)
    {
      return defaultValue;
    }
    if (index == args.Length - 1 ||
        !int.TryParse(
            args[index + 1],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value) ||
        value < minimum ||
        value > maximum)
    {
      throw new InvalidDataException(
          "A bounded canary argument is invalid.");
    }
    return value;
  }

  private static string ReadRequiredEnvironment(string name)
  {
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value)
        ? throw new InvalidDataException(
            "A required canary environment value is missing.")
        : value;
  }

  private static string EnsureOrigin(string value)
  {
    var uri = new Uri(value, UriKind.Absolute);
    return uri.GetLeftPart(UriPartial.Authority) + "/";
  }

  private static int Fail(string category)
  {
    Console.Error.WriteLine($"canary-failed:{category}");
    return 1;
  }
}
