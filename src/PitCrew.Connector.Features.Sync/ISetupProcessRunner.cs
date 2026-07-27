namespace PitCrew.Connector.Features.Sync;

internal interface ISetupProcessRunner
{
  Task<SetupProcessResult> RunAsync(
      SetupProcessRequest request,
      CancellationToken cancellationToken);
}
