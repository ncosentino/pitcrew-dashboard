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

  void AssertUnrelatedStateUnchanged();
}
