namespace PitCrew.Connector.Features.Sync.Tests;

internal sealed class FakeSetupProcessRunner : ISetupProcessRunner
{
  public SetupProcessRequest? LastRequest { get; private set; }

  public Func<SetupProcessRequest, CancellationToken, Task<SetupProcessResult>>
      Handler { get; set; } =
      static (_, _) => Task.FromResult(
          new SetupProcessResult(0, false));

  public async Task<SetupProcessResult> RunAsync(
      SetupProcessRequest request,
      CancellationToken cancellationToken)
  {
    LastRequest = request;
    return await Handler(request, cancellationToken);
  }
}
