using PitCrew.Support.Canary.Contracts;
using PitCrew.Support.Protocol;

namespace PitCrew.Support.Canary.Scenarios;

/// <summary>
/// Exercises every closed read-only support diagnostic mode through one
/// enrolled candidate node.
/// </summary>
public sealed class SupportDiagnosticModeMatrixScenario : ICanaryScenario
{
  private const string ScenarioId =
      "support-diagnostic-mode-matrix-v1";
  private readonly SupportFreshEnrollmentDiagnosticScenario _inner =
      new(
          ScenarioId,
          [],
          afterFirstAcceptedPoll: null,
          SupportDiagnosticModes.All);

  /// <inheritdoc />
  public string Id => _inner.Id;

  /// <inheritdoc />
  public IReadOnlySet<string> RequiredCapabilities =>
      _inner.RequiredCapabilities;

  /// <inheritdoc />
  public Task<CanaryScenarioResult> RunAsync(
      CanaryRuntimeManifest runtime,
      CanaryScenarioContext context,
      CancellationToken cancellationToken) =>
      _inner.RunAsync(
          runtime,
          context,
          cancellationToken);
}
