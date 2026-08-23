namespace PitCrew.Support.Canary.Scenarios;

internal static class CandidatePaths
{
  public static string ResolveAssembly(
      string dashboardSourceRoot,
      string projectName)
  {
    var configuration = Environment.GetEnvironmentVariable(
        "PITCREW_CANARY_DOTNET_CONFIGURATION") ?? "Debug";
    if (configuration is not ("Debug" or "Release"))
    {
      throw new CanaryScenarioFailureException(
          "candidate-configuration-invalid");
    }
    var path = Path.Combine(
        dashboardSourceRoot,
        "src",
        projectName,
        "bin",
        configuration,
        "net10.0",
        $"{projectName}.dll");
    return File.Exists(path)
        ? path
        : throw new CanaryScenarioFailureException(
            "candidate-assembly-missing");
  }
}
