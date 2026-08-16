using System.Diagnostics;
using System.Text.Json;

using PitCrew.Protocol;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Broker.App;

internal sealed class SupportDiagnosticsBroker(SupportBrokerOptions _options)
{
  private const string RelativeCollectorPath =
      "plugins\\pitcrew-operations\\skills\\pitcrew-remote-diagnostics\\scripts\\Collect-PitCrewDiagnostics.ps1";

  public async Task<SupportBrokerExecution> ExecuteAsync(
      SupportBrokerRequest request,
      CancellationToken cancellationToken)
  {
    if (!SupportDiagnosticModes.IsSupported(request.DiagnosticMode))
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.InvalidMode,
          null,
          "Diagnostic mode is not allowed.");
    }
    if (request.ProfileId is not null &&
        (!PitCrewProfileId.IsValid(request.ProfileId) || !ProfileExists(request.ProfileId)))
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.InvalidProfile,
          null,
          "Profile ID is not locally configured.");
    }
    if (!IsPackageIdValid(request.PackageId))
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.ExecutionFailed,
          null,
          "Package ID is not a deterministic lowercase hexadecimal identifier.");
    }
    var scriptPath = Path.Combine(_options.PitCrewRoot, RelativeCollectorPath);
    if (!File.Exists(scriptPath))
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.ScriptMissing,
          null,
          "The fixed diagnostics collector is not installed.");
    }

    var collectorCommand =
        $"& {Quote(scriptPath)} -PitCrewRoot {Quote(_options.PitCrewRoot)} -FileOnly -PassThruOnly -DiagnosticMode {Quote(request.DiagnosticMode)} -PackageId {Quote(request.PackageId)}";
    if (request.ProfileId is not null)
    {
      collectorCommand += $" -Profile {Quote(request.ProfileId)}";
    }
    collectorCommand += " | ConvertTo-Json -Depth 100 -Compress";
    var arguments = new List<string>
    {
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-Command",
        collectorCommand,
    };

    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
      FileName = "pwsh",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    foreach (var argument in arguments)
    {
      process.StartInfo.ArgumentList.Add(argument);
    }
    if (!process.Start())
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.ExecutionFailed,
          null,
          "The diagnostics collector could not be started.");
    }
    var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
    var error = await process.StandardError.ReadToEndAsync(cancellationToken);
    await process.WaitForExitAsync(cancellationToken);
    if (process.ExitCode != 0)
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.ExecutionFailed,
          null,
          string.IsNullOrWhiteSpace(error) ? "The diagnostics collector failed." : error.Trim());
    }
    return ParseResponse(output);
  }

  private bool ProfileExists(string profileId)
  {
    var profilesRoot = Path.Combine(_options.PitCrewRoot, "profiles");
    var profileDirectory = Path.Combine(profilesRoot, profileId);
    var fullProfilesRoot = Path.GetFullPath(profilesRoot);
    var fullProfileDirectory = Path.GetFullPath(profileDirectory);
    return fullProfileDirectory.StartsWith(fullProfilesRoot, StringComparison.OrdinalIgnoreCase) &&
        Directory.Exists(fullProfileDirectory);
  }

  private static bool IsPackageIdValid(string packageId) =>
      packageId.Length is >= 16 and <= 64 &&
      packageId.All(character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static string Quote(string value) =>
      "'" + value.Replace("'", "''") + "'";

  private static SupportBrokerExecution ParseResponse(string output)
  {
    try
    {
      using var document = JsonDocument.Parse(output);
      var root = document.RootElement;
      if (!root.TryGetProperty("report", out var report) ||
          !root.TryGetProperty("markdown", out var markdownElement) ||
          markdownElement.ValueKind != JsonValueKind.String)
      {
        return new SupportBrokerExecution(
            SupportBrokerStatus.ExecutionFailed,
            null,
            "Collector output omitted report or markdown.");
      }
      return new SupportBrokerExecution(
          SupportBrokerStatus.Succeeded,
          new SupportBrokerResponse(report.Clone(), markdownElement.GetString() ?? string.Empty),
          null);
    }
    catch (JsonException)
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.ExecutionFailed,
          null,
          "Collector output was not valid JSON.");
    }
  }
}
