using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Broker.App;

internal sealed class SupportDiagnosticsBroker
{
  private const int MaximumCollectorOutputBytes = 4_194_304;

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

    var initialSessionState = InitialSessionState.CreateDefault();
    if (OperatingSystem.IsWindows())
    {
      initialSessionState.ExecutionPolicy =
          Microsoft.PowerShell.ExecutionPolicy.Bypass;
    }
    using var powerShell = PowerShell.Create(initialSessionState);
    powerShell
        .AddCommand(evidence.CollectorPath!)
        .AddParameter("PitCrewRoot", _options.PitCrewRoot)
        .AddParameter("FileOnly")
        .AddParameter("PassThruOnly")
        .AddParameter("DiagnosticMode", request.DiagnosticMode)
        .AddParameter("PackageId", request.PackageId)
        .AddParameter("Profile", evidence.ProfileId!)
        .AddCommand("ConvertTo-Json")
        .AddParameter("Depth", 100)
        .AddParameter("Compress");
    using var cancellationRegistration = cancellationToken.Register(
        static state => ((PowerShell)state!).Stop(),
        powerShell);
    PSDataCollection<PSObject>? output = null;
    try
    {
      output = await powerShell.InvokeAsync();
    }
    catch (PipelineStoppedException) when (cancellationToken.IsCancellationRequested)
    {
      throw new OperationCanceledException(cancellationToken);
    }
    using (output)
    {
      if (powerShell.HadErrors)
      {
        return new SupportBrokerExecution(
            SupportBrokerStatus.ExecutionFailed,
            null,
            "The diagnostics collector failed.");
      }
      var serialized = string.Concat(
          output.Select(item => item.BaseObject as string ?? item.ToString()));
      if (System.Text.Encoding.UTF8.GetByteCount(serialized) >
          MaximumCollectorOutputBytes)
      {
        return new SupportBrokerExecution(
            SupportBrokerStatus.ExecutionFailed,
            null,
            "The diagnostics collector returned an oversized response.");
      }
      return ParseResponse(serialized);
    }
  }

  private static bool IsPackageIdValid(string packageId) =>
      packageId.Length is >= 16 and <= 64 &&
      packageId.All(character =>
          character is >= '0' and <= '9' or >= 'a' and <= 'f');

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
