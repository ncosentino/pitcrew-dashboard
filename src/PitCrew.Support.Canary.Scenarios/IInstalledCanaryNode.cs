namespace PitCrew.Support.Canary.Scenarios;

internal interface IInstalledCanaryNode : IAsyncDisposable
{
  string AgentStateRoot { get; }

  string InstallationCategory { get; }

  Task InstallAsync(
      string dashboardUrl,
      string enrollmentCode,
      CancellationToken cancellationToken);

  Task FinalizeAndRestartAsync(
      CancellationToken cancellationToken);

  Task DeleteKeysAndUninstallAsync(
      CancellationToken cancellationToken);

  Task WaitForAcceptedPollAsync(
      CancellationToken cancellationToken);

  Task<string?> ReadRequestDispositionAsync(
      CancellationToken cancellationToken);

  Task<string?> ObserveRequestFailureAsync(
      CancellationToken cancellationToken);

  void AssertUnrelatedStateUnchanged();
}
