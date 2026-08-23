using PitCrew.Support.Canary.Contracts;

namespace PitCrew.Support.Canary.Scenarios;

/// <summary>
/// Provides non-serialized local paths required by a scenario runner.
/// </summary>
/// <param name="RunRoot">Exact run-scoped workspace.</param>
/// <param name="DashboardSourceRoot">Exact Dashboard candidate checkout.</param>
/// <param name="PitCrewSourceRoot">Exact PitCrew candidate checkout.</param>
/// <param name="TimeProvider">Clock used for bounded scenario evidence.</param>
public sealed record CanaryScenarioContext(
    string RunRoot,
    string DashboardSourceRoot,
    string PitCrewSourceRoot,
    TimeProvider TimeProvider);

/// <summary>
/// Defines one additive canary scenario.
/// </summary>
public interface ICanaryScenario
{
  /// <summary>
  /// Gets the stable scenario identifier.
  /// </summary>
  string Id { get; }

  /// <summary>
  /// Gets the runtime capabilities required before execution.
  /// </summary>
  IReadOnlySet<string> RequiredCapabilities { get; }

  /// <summary>
  /// Executes the scenario against an already-running topology.
  /// </summary>
  /// <param name="runtime">Validated non-secret runtime manifest.</param>
  /// <param name="context">Run-local paths excluded from persisted evidence.</param>
  /// <param name="cancellationToken">Bounded execution cancellation.</param>
  /// <returns>A terminal redacted result.</returns>
  Task<CanaryScenarioResult> RunAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      CancellationToken cancellationToken);
}
