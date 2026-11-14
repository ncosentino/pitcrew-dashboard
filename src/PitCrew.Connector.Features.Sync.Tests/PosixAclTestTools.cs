using System.Diagnostics;

namespace PitCrew.Connector.Features.Sync.Tests;

internal static class PosixAclTestTools
{
  internal static Task<string> ReadAsync(
      string path,
      CancellationToken cancellationToken) =>
      ExecuteAsync("getfacl", ["-cpn", "--", path], cancellationToken);

  internal static Task<string> SetAsync(
      string path,
      string entries,
      CancellationToken cancellationToken) =>
      ExecuteAsync("setfacl", ["-m", entries, "--", path], cancellationToken);

  private static async Task<string> ExecuteAsync(
      string tool,
      string[] arguments,
      CancellationToken cancellationToken)
  {
    var start = new ProcessStartInfo(tool)
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    foreach (var argument in arguments)
    {
      start.ArgumentList.Add(argument);
    }
    using var process = new Process { StartInfo = start };
    using var timeout = new CancellationTokenSource(
        TimeSpan.FromSeconds(30), TimeProvider.System);
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken, timeout.Token);
    if (!process.Start())
    {
      throw new InvalidOperationException("The POSIX ACL test tool did not start.");
    }
    try
    {
      var output = process.StandardOutput.ReadToEndAsync(linked.Token);
      var error = process.StandardError.ReadToEndAsync(linked.Token);
      await process.WaitForExitAsync(linked.Token);
      var result = await output;
      _ = await error;
      if (process.ExitCode != 0)
      {
        throw new InvalidOperationException(
            $"The POSIX ACL test tool failed with exit code {process.ExitCode}.");
      }
      return result.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
    finally
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }
    }
  }
}
