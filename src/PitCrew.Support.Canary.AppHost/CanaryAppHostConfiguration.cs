namespace PitCrew.Support.Canary.AppHost;

internal static class CanaryAppHostConfiguration
{
  public static string ReadAbsolutePath(string name)
  {
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value) ||
        !Path.IsPathFullyQualified(value))
    {
      throw new InvalidOperationException(
          $"Canary AppHost configuration '{name}' is invalid.");
    }
    return Path.GetFullPath(value);
  }

  public static string ReadConfiguration()
  {
    var value = Environment.GetEnvironmentVariable(
        "PITCREW_CANARY_DOTNET_CONFIGURATION") ?? "Debug";
    return value is "Debug" or "Release"
        ? value
        : throw new InvalidOperationException(
            "Canary AppHost build configuration is invalid.");
  }

  public static string ReadSecret()
  {
    var value = Environment.GetEnvironmentVariable(
        "PITCREW_CANARY_RELAY_SECRET");
    if (value is not { Length: >= 32 and <= 256 } ||
        value.Contains('\r') ||
        value.Contains('\n'))
    {
      throw new InvalidOperationException(
          "Canary AppHost relay secret configuration is invalid.");
    }
    return value;
  }

  public static string ReadRunId()
  {
    var value = Environment.GetEnvironmentVariable(
        "PITCREW_CANARY_RUN_ID");
    if (value is not { Length: 32 } ||
        value.Any(character =>
            character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
    {
      throw new InvalidOperationException(
          "Canary AppHost run identity is invalid.");
    }
    return value;
  }

  public static string ResolveCandidateAssembly(
      string sourceRoot,
      string projectName,
      string configuration)
  {
    var path = Path.Combine(
        sourceRoot,
        "src",
        projectName,
        "bin",
        configuration,
        "net10.0",
        $"{projectName}.dll");
    return File.Exists(path)
        ? path
        : throw new InvalidOperationException(
            $"Candidate assembly '{projectName}' is unavailable.");
  }
}
