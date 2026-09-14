namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Versioned portable fleet-trust scenario corpus.
/// </summary>
/// <param name="SchemaVersion">Corpus schema version.</param>
/// <param name="CurrentBehavior">Characterized behavior before projection or grouping changes.</param>
/// <param name="FleetEvidence">Fleet reporting and evidence-authority scenarios.</param>
/// <param name="IncidentCardinalities">Bounded incident query scenarios.</param>
/// <param name="ConditionDecomposition">Persistent and independent condition scenarios.</param>
/// <param name="SupportDiagnostics">Support diagnostic outcome scenarios.</param>
public sealed record FleetTrustScenarioSet(
    int SchemaVersion,
    IReadOnlyList<string> CurrentBehavior,
    IReadOnlyList<FleetEvidenceScenario> FleetEvidence,
    IReadOnlyList<IncidentCardinalityScenario> IncidentCardinalities,
    IReadOnlyList<ConditionScenario> ConditionDecomposition,
    IReadOnlyList<SupportDiagnosticScenario> SupportDiagnostics);
