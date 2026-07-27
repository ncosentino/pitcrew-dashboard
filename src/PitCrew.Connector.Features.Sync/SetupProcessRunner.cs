using System.Diagnostics;

using NexusLabs.Needlr;

namespace PitCrew.Connector.Features.Sync;

[DoNotAutoRegister]
internal sealed class SetupProcessRunner : ISetupProcessRunner
{
  public async Task<SetupProcessResult> RunAsync(
      SetupProcessRequest request,
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
      return new SetupProcessResult(null, false);
    }
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken);
    timeoutSource.CancelAfter(request.Timeout);
    try
    {
      await process.WaitForExitAsync(timeoutSource.Token);
      return new SetupProcessResult(process.ExitCode, false);
    }
    catch (OperationCanceledException)
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(CancellationToken.None);
      }
      cancellationToken.ThrowIfCancellationRequested();
      return new SetupProcessResult(null, true);
    }
  }
}
