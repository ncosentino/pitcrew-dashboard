using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PitCrew.Support.Canary.Scenarios;

internal sealed class LinuxCanaryCommandRunner(string _workingDirectory)
{
  private const int MaximumPrivilegedFileBytes = 4096;
  private const int MaximumCommandOutputBytes = 32768;
  private readonly IReadOnlyDictionary<string, string> _emptyEnvironment =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

  public Task<int> RunSudoAsync(
      IReadOnlyList<string> arguments,
      TimeSpan timeout,
      CancellationToken cancellationToken)
  {
    var sudoArguments = new List<string>
    {
        "-n",
    };
    sudoArguments.AddRange(arguments);
    return CandidateProcess.RunToolAsync(
        "sudo",
        _workingDirectory,
        _emptyEnvironment,
        sudoArguments,
        timeout,
        cancellationToken);
  }

  public async Task<bool> AccountExistsAsync(
      string database,
      string identity,
      CancellationToken cancellationToken)
  {
    var exitCode = await CandidateProcess.RunToolAsync(
        "getent",
        _workingDirectory,
        _emptyEnvironment,
        [database, identity],
        TimeSpan.FromSeconds(10),
        cancellationToken);
    return exitCode switch
    {
      0 => true,
      2 => false,
      _ => throw new CanaryScenarioFailureException(
          "linux-service-inspection-failed"),
    };
  }

  public async Task<string?> ReadPrivilegedFileAsync(
      string path,
      bool allowUnavailable,
      CancellationToken cancellationToken)
  {
    var result = await RunCapturedAsync(
        "sudo",
        [
            "-n",
            "head",
            "-c",
            (MaximumPrivilegedFileBytes + 1).ToString(
                CultureInfo.InvariantCulture),
            "--",
            path,
        ],
        TimeSpan.FromSeconds(10),
        cancellationToken);
    if (result.ExitCode != 0)
    {
      return allowUnavailable
          ? null
          : throw new CanaryScenarioFailureException(
              "linux-service-inspection-failed");
    }
    if (Encoding.UTF8.GetByteCount(result.StandardOutput) >
        MaximumPrivilegedFileBytes)
    {
      throw new CanaryScenarioFailureException(
          "linux-service-inspection-failed");
    }
    return result.StandardOutput;
  }

  public async Task<string?> ReadCommandOutputAsync(
      string fileName,
      IReadOnlyList<string> arguments,
      CancellationToken cancellationToken)
  {
    var result = await RunCapturedAsync(
        fileName,
        arguments,
        TimeSpan.FromSeconds(10),
        cancellationToken);
    if (result.ExitCode != 0)
    {
      return null;
    }
    if (Encoding.UTF8.GetByteCount(result.StandardOutput) >
        MaximumCommandOutputBytes)
    {
      throw new CanaryScenarioFailureException(
          "linux-service-inspection-failed");
    }
    return result.StandardOutput.Trim();
  }

  private async Task<CapturedProcessResult> RunCapturedAsync(
      string fileName,
      IReadOnlyList<string> arguments,
      TimeSpan timeout,
      CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = fileName,
      WorkingDirectory = _workingDirectory,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }
    using var process = Process.Start(startInfo) ??
        throw new IOException(
            "The Linux canary inspection process did not start.");
    var standardOutput = process.StandardOutput.ReadToEndAsync(
        cancellationToken);
    var standardError = process.StandardError.ReadToEndAsync(
        cancellationToken);
    using var timeoutSource =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
    timeoutSource.CancelAfter(timeout);
    try
    {
      await process.WaitForExitAsync(timeoutSource.Token);
    }
    catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      process.Kill(entireProcessTree: true);
      throw new CanaryScenarioFailureException(
          "canary-tool-timeout");
    }
    var output = await standardOutput;
    _ = await standardError;
    return new CapturedProcessResult(
        process.ExitCode,
        output);
  }

  private sealed record CapturedProcessResult(
      int ExitCode,
      string StandardOutput);
}
