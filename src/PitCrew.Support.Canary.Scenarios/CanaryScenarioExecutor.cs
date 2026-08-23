using System.Diagnostics;

using PitCrew.Support.Canary.Contracts;

namespace PitCrew.Support.Canary.Scenarios;

/// <summary>
/// Enforces one total scenario deadline and translates terminal failures into
/// bounded evidence.
/// </summary>
public static class CanaryScenarioExecutor
{
  private static readonly TimeSpan _cleanupGrace =
      TimeSpan.FromSeconds(5);

  /// <summary>
  /// Executes one scenario with a total deadline.
  /// </summary>
  /// <param name="scenario">Registered scenario implementation.</param>
  /// <param name="runtime">Validated runtime manifest.</param>
  /// <param name="context">Run-local scenario context.</param>
  /// <param name="timeout">Total execution deadline.</param>
  /// <param name="cancellationToken">Caller cancellation.</param>
  /// <returns>
  /// The scenario result or a bounded timeout, cancellation, or unexpected
  /// failure result.
  /// </returns>
  public static async Task<CanaryScenarioResult> RunAsync(
      ICanaryScenario scenario,
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      TimeSpan timeout,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(scenario);
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(context);
    if (timeout <= TimeSpan.Zero ||
        timeout > TimeSpan.FromMinutes(30))
    {
      throw new ArgumentOutOfRangeException(
          nameof(timeout));
    }
    var startedAt = context.TimeProvider.GetUtcNow();
    var stopwatch = Stopwatch.StartNew();
    using var timeoutSource =
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
    timeoutSource.CancelAfter(timeout);
    Task<CanaryScenarioResult> scenarioTask;
    try
    {
      scenarioTask = scenario.RunAsync(
          runtime,
          context,
          timeoutSource.Token);
    }
    catch (Exception)
    {
      return Failure(
          runtime,
          scenario.Id,
          "scenario-unexpected-failure",
          startedAt,
          stopwatch.ElapsedMilliseconds,
          context.TimeProvider);
    }
    try
    {
      return await scenarioTask.WaitAsync(
          timeout,
          cancellationToken);
    }
    catch (TimeoutException)
    {
      await timeoutSource.CancelAsync();
      await ObserveCleanupAsync(
          scenarioTask,
          CancellationToken.None);
      return Failure(
          runtime,
          scenario.Id,
          "scenario-timeout",
          startedAt,
          stopwatch.ElapsedMilliseconds,
          context.TimeProvider);
    }
    catch (OperationCanceledException)
    {
      await timeoutSource.CancelAsync();
      await ObserveCleanupAsync(
          scenarioTask,
          CancellationToken.None);
      return Failure(
          runtime,
          scenario.Id,
          cancellationToken.IsCancellationRequested
              ? "scenario-cancelled"
              : "scenario-timeout",
          startedAt,
          stopwatch.ElapsedMilliseconds,
          context.TimeProvider);
    }
    catch (Exception)
    {
      return Failure(
          runtime,
          scenario.Id,
          "scenario-unexpected-failure",
          startedAt,
          stopwatch.ElapsedMilliseconds,
          context.TimeProvider);
    }
  }

  private static async Task ObserveCleanupAsync(
      Task scenarioTask,
      CancellationToken cancellationToken)
  {
#pragma warning disable VSTHRD003 // The registered scenario task is the extension boundary being supervised.
    var completed = await Task.WhenAny(
        scenarioTask,
        Task.Delay(
            _cleanupGrace,
            TimeProvider.System,
            cancellationToken));
#pragma warning restore VSTHRD003
    if (completed == scenarioTask &&
        scenarioTask.IsFaulted)
    {
      _ = scenarioTask.Exception;
    }
  }

  private static CanaryScenarioResult Failure(
      CanaryRuntimeManifest runtime,
      string scenarioId,
      string category,
      DateTimeOffset startedAt,
      long durationMilliseconds,
      TimeProvider timeProvider) =>
      new(
          CanaryManifestFile.ScenarioResultSchemaVersion,
          runtime.RunId,
          scenarioId,
          runtime.TopologyProfile,
          "failed",
          category,
          [
              new CanaryScenarioStepResult(
                  "scenario-execution",
                  "failed",
                  category,
                  Math.Clamp(
                      durationMilliseconds,
                      0,
                      1_800_000)),
          ],
          startedAt,
          timeProvider.GetUtcNow());
}
