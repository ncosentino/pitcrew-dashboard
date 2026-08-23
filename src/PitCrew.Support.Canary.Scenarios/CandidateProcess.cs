using System.Diagnostics;

namespace PitCrew.Support.Canary.Scenarios;

internal sealed class CandidateProcess : IAsyncDisposable
{
  private readonly Process _process;

  private CandidateProcess(Process process)
  {
    _process = process;
  }

  public bool HasExited => _process.HasExited;

  public int ExitCode => _process.ExitCode;

  public static CandidateProcess Start(
      string assemblyPath,
      string workingDirectory,
      IReadOnlyDictionary<string, string> environment,
      params string[] arguments)
  {
    if (!File.Exists(assemblyPath) ||
        !Directory.Exists(workingDirectory))
    {
      throw new IOException(
          "A candidate process input is unavailable.");
    }
    var startInfo = new ProcessStartInfo
    {
      FileName = Environment.GetEnvironmentVariable(
          "DOTNET_HOST_PATH") ?? "dotnet",
      WorkingDirectory = workingDirectory,
      UseShellExecute = false,
    };
    startInfo.ArgumentList.Add(assemblyPath);
    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }
    foreach (var pair in environment)
    {
      startInfo.Environment[pair.Key] = pair.Value;
    }
    var process = Process.Start(startInfo) ??
        throw new IOException(
            "The candidate process did not start.");
    return new CandidateProcess(process);
  }

  public static async Task<int> RunAsync(
      string assemblyPath,
      string workingDirectory,
      IReadOnlyDictionary<string, string> environment,
      TimeSpan timeout,
      CancellationToken cancellationToken,
      params string[] arguments)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = Environment.GetEnvironmentVariable(
          "DOTNET_HOST_PATH") ?? "dotnet",
      WorkingDirectory = workingDirectory,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    startInfo.ArgumentList.Add(assemblyPath);
    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }
    foreach (var pair in environment)
    {
      startInfo.Environment[pair.Key] = pair.Value;
    }
    using var process = Process.Start(startInfo) ??
        throw new IOException(
            "The candidate command did not start.");
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
          "candidate-command-timeout");
    }
    return process.ExitCode;
  }

  public static async Task<int> RunToolAsync(
      string fileName,
      string workingDirectory,
      IReadOnlyDictionary<string, string> environment,
      IReadOnlyList<string> arguments,
      TimeSpan timeout,
      CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = fileName,
      WorkingDirectory = workingDirectory,
      UseShellExecute = false,
    };
    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }
    foreach (var pair in environment)
    {
      startInfo.Environment[pair.Key] = pair.Value;
    }
    using var process = Process.Start(startInfo) ??
        throw new IOException(
            "The canary tool process did not start.");
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
    return process.ExitCode;
  }

  public async Task WaitForExitAsync(
      TimeSpan timeout,
      CancellationToken cancellationToken)
  {
    using var timeoutSource =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
    timeoutSource.CancelAfter(timeout);
    try
    {
      await _process.WaitForExitAsync(timeoutSource.Token);
    }
    catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
    {
      throw new CanaryScenarioFailureException(
          "candidate-process-exit-timeout");
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (!_process.HasExited)
    {
      _process.Kill(entireProcessTree: true);
      using var timeout = new CancellationTokenSource(
          TimeSpan.FromSeconds(10));
      await _process.WaitForExitAsync(timeout.Token);
    }
    _process.Dispose();
  }
}
