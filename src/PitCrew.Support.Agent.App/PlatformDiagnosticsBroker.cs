namespace PitCrew.Support.Agent.App;

internal sealed class PlatformDiagnosticsBroker : ILocalDiagnosticsBroker
{
  private readonly ILocalDiagnosticsBroker _inner;

  public PlatformDiagnosticsBroker(SupportAgentOptions options)
  {
    if (OperatingSystem.IsWindows())
    {
      _inner = new NamedPipeDiagnosticsBroker(options.PipeName);
      return;
    }
    if (OperatingSystem.IsLinux())
    {
      _inner = new UnixSocketDiagnosticsBroker(options.SocketPath);
      return;
    }
    throw new PlatformNotSupportedException(
        "PitCrew support agent IPC supports Windows and Linux only.");
  }

  public Task<LocalDiagnosticsResult> ExecuteAsync(
      LocalDiagnosticsRequest request,
      CancellationToken cancellationToken) =>
      _inner.ExecuteAsync(request, cancellationToken);
}
