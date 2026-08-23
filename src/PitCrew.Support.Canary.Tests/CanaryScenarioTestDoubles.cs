using PitCrew.Support.Canary.Contracts;
using PitCrew.Support.Canary.Scenarios;

namespace PitCrew.Support.Canary.Tests;

internal sealed class CancellingCanaryScenario : ICanaryScenario
{
  private readonly TaskCompletionSource _cancellation =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

  public string Id => "cancelling-test-v1";

  public IReadOnlySet<string> RequiredCapabilities { get; } =
      new HashSet<string>(
          StringComparer.OrdinalIgnoreCase);

  public Task CleanupObserved => _cancellation.Task;

  public async Task<CanaryScenarioResult> RunAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      CancellationToken cancellationToken)
  {
    var completion = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    using var registration = cancellationToken.Register(
        () => completion.TrySetCanceled(
            cancellationToken));
    try
    {
      await completion.Task;
      throw new InvalidOperationException(
          "The cancellation signal did not stop the test scenario.");
    }
    finally
    {
      _cancellation.TrySetResult();
    }
  }
}

internal sealed class ThrowingCanaryScenario : ICanaryScenario
{
  public string Id => "throwing-test-v1";

  public IReadOnlySet<string> RequiredCapabilities { get; } =
      new HashSet<string>(
          StringComparer.OrdinalIgnoreCase);

  public Task<CanaryScenarioResult> RunAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      CancellationToken cancellationToken) =>
      Task.FromException<CanaryScenarioResult>(
          new InvalidOperationException(
              "C:\\private\\credential-value"));
}
