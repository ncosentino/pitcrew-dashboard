namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Captures one fleet reporting and retained-evidence combination.
/// </summary>
/// <param name="Id">Stable scenario identifier.</param>
/// <param name="Description">Sanitized human-readable purpose.</param>
/// <param name="ConnectorReporting">Whether connector contact is current or lost.</param>
/// <param name="Clocks">Independent evidence-processing clocks.</param>
/// <param name="Claims">Claim-level authority states.</param>
/// <param name="ExpectedInterpretation">Interpretation later projections must preserve.</param>
/// <param name="TrueResolutionProven">Whether fresh authoritative evidence proves the condition cleared.</param>
public sealed record FleetEvidenceScenario(
    string Id,
    string Description,
    string ConnectorReporting,
    ScenarioClocks Clocks,
    IReadOnlyList<EvidenceClaim> Claims,
    string ExpectedInterpretation,
    bool TrueResolutionProven);
