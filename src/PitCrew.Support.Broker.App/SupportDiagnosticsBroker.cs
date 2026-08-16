using System.Buffers;
using System.Diagnostics;
using System.Text;
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
    var outputTask = ReadBoundedUtf8Async(
        process.StandardOutput.BaseStream,
        MaximumCollectorOutputBytes,
        cancellationToken);
    var errorTask = DrainAsync(
        process.StandardError.BaseStream,
        cancellationToken);
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
    if (output is null)
    {
      return new SupportBrokerExecution(
          SupportBrokerStatus.ExecutionFailed,
          null,
          "The diagnostics collector returned an oversized response.");
    }
    return ParseResponse(output);
  }

  private static async Task<string?> ReadBoundedUtf8Async(
      Stream stream,
      int maximumBytes,
      CancellationToken cancellationToken)
  {
    var buffer = ArrayPool<byte>.Shared.Rent(8192);
    using var output = new MemoryStream(
        Math.Min(maximumBytes, 65_536));
    var exceeded = false;
    try
    {
      while (true)
      {
        var read = await stream.ReadAsync(
            buffer.AsMemory(),
            cancellationToken);
        if (read == 0)
        {
          break;
        }
        if (exceeded)
        {
          continue;
        }
        if (output.Length + read > maximumBytes)
        {
          exceeded = true;
          continue;
        }
        await output.WriteAsync(
            buffer.AsMemory(0, read),
            cancellationToken);
      }
      return exceeded
          ? null
          : Encoding.UTF8.GetString(
              output.GetBuffer(),
              0,
              checked((int)output.Length));
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buffer);
    }
  }

  private static async Task DrainAsync(
      Stream stream,
      CancellationToken cancellationToken)
  {
    var buffer = ArrayPool<byte>.Shared.Rent(4096);
    try
    {
      while (await stream.ReadAsync(
          buffer.AsMemory(),
          cancellationToken) != 0)
      {
      }
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buffer);
    }
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
