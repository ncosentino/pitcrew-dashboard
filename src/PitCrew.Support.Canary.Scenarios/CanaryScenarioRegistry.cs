namespace PitCrew.Support.Canary.Scenarios;

/// <summary>
/// Resolves registered scenarios without coupling topology startup to scenario
/// implementation.
/// </summary>
public static class CanaryScenarioRegistry
{
  private static readonly IReadOnlyDictionary<string, ICanaryScenario> _scenarios =
      new ICanaryScenario[]
      {
        new SupportFreshEnrollmentDiagnosticScenario(),
        new SupportRelayRestartRecoveryScenario(),
        new TopologySmokeScenario(),
      }.ToDictionary(
          scenario => scenario.Id,
          StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// Gets all registered scenario identifiers.
  /// </summary>
  public static IReadOnlyCollection<string> ScenarioIds =>
      _scenarios.Keys.ToArray();

  /// <summary>
  /// Resolves an exact registered scenario.
  /// </summary>
  /// <param name="scenarioId">Stable scenario identifier.</param>
  /// <returns>The registered scenario, or <see langword="null"/>.</returns>
  public static ICanaryScenario? ResolveOrNull(string scenarioId) =>
      _scenarios.GetValueOrDefault(scenarioId);
}
