namespace PitCrew.Connector.Features.Sync.Tests;

internal sealed class FakeCapacityProcessRunner : ICapacityProcessRunner
{
  public CapacityProcessRequest? LastRequest { get; private set; }

  public Func<CapacityProcessRequest, CancellationToken, Task<CapacityProcessResult>>
      Handler { get; set; } =
      static (_, _) => Task.FromResult(
          new CapacityProcessResult(0, false));

  public async Task<CapacityProcessResult> RunAsync(
      CapacityProcessRequest request,
      CancellationToken cancellationToken)
  {
    LastRequest = request;
    return await Handler(request, cancellationToken);
  }
}
