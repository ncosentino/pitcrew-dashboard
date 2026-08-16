using System.Diagnostics;
using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Broker.App;

internal sealed class SupportDiagnosticsBroker
{
  private readonly SupportBrokerOptions _options;
  private readonly SupportEvidenceAccessValidator _evidenceValidator;

  public SupportDiagnosticsBroker(SupportBrokerOptions options)
      : this(options, SupportEvidencePolicy.Load())
  {
  }

  internal SupportDiagnosticsBroker(
      SupportBrokerOptions options,
      SupportEvidencePolicyDocument policy)
  {
    _options = options;
    _evidenceValidator = new SupportEvidenceAccessValidator(
        options,
        policy);
  }

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
    if (!IsPackageIdValid(request.PackageId))
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.ExecutionFailed,
          null,
          "Package ID is not a deterministic lowercase hexadecimal identifier.");
    }
    var evidence = _evidenceValidator.Validate(request.ProfileId);
    if (!evidence.Succeeded)
    {
      return new SupportBrokerExecution(
          evidence.Status,
          null,
          evidence.Error);
    }

    var collectorCommand =
        $"& {Quote(evidence.CollectorPath!)} -PitCrewRoot {Quote(_options.PitCrewRoot)} -FileOnly -PassThruOnly -DiagnosticMode {Quote(request.DiagnosticMode)} -PackageId {Quote(request.PackageId)} -Profile {Quote(evidence.ProfileId!)}";
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
    var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
    try
    {
      await Task.WhenAll(
          outputTask,
          errorTask,
          process.WaitForExitAsync(cancellationToken));
    }
    catch (OperationCanceledException)
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }
      throw;
    }
    if (process.ExitCode != 0)
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.ExecutionFailed,
          null,
          "The diagnostics collector failed.");
    }
    var output = await outputTask;
    if (output.Length > 4_194_304)
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.ExecutionFailed,
          null,
          "The diagnostics collector returned an oversized response.");
    }
    return ParseResponse(output);
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
