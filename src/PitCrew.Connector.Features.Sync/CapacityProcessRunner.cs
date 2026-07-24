using System.Diagnostics;

using NexusLabs.Needlr;

namespace PitCrew.Connector.Features.Sync;

internal sealed record CapacityProcessRequest(
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

internal sealed record CapacityProcessResult(
    int? ExitCode,
    bool TimedOut);

internal interface ICapacityProcessRunner
{
  Task<CapacityProcessResult> RunAsync(
      CapacityProcessRequest request,
      CancellationToken cancellationToken);
}

[DoNotAutoRegister]
internal sealed class CapacityProcessRunner : ICapacityProcessRunner
{
  public async Task<CapacityProcessResult> RunAsync(
      CapacityProcessRequest request,
      CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = request.Executable,
      WorkingDirectory = request.WorkingDirectory,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
    };
    foreach (var argument in request.Arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    using var process = new Process
    {
      StartInfo = startInfo,
      EnableRaisingEvents = true,
    };
    process.OutputDataReceived += static (_, _) => { };
    process.ErrorDataReceived += static (_, _) => { };
    if (!process.Start())
    {
      return new CapacityProcessResult(null, false);
    }
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken);
    timeoutSource.CancelAfter(request.Timeout);
    try
    {
      await process.WaitForExitAsync(timeoutSource.Token);
      return new CapacityProcessResult(process.ExitCode, false);
    }
    catch (OperationCanceledException)
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(CancellationToken.None);
      }
      cancellationToken.ThrowIfCancellationRequested();
      return new CapacityProcessResult(null, true);
    }
  }
}
