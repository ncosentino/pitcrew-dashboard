namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Defines one bounded support-diagnostic outcome and retrieval expectation.
/// </summary>
/// <param name="Id">Stable scenario identifier.</param>
/// <param name="SessionId">Stable synthetic session identifier.</param>
/// <param name="DiagnosticMode">Closed diagnostic mode.</param>
/// <param name="ProfileId">Explicit profile, or <see langword="null"/> when omitted.</param>
/// <param name="Clocks">Independent evidence-processing clocks.</param>
/// <param name="Outcome">Completed, rejected, timed-out, forbidden, or partial outcome.</param>
/// <param name="RejectionDisposition">Bounded rejection reason, when applicable.</param>
/// <param name="FailureStage">Closed stage at which the request failed, or none.</param>
/// <param name="ReturnContext">Whether exact retrieval uses a fresh tenant/session context.</param>
/// <param name="Claims">Claim-level authority states.</param>
/// <param name="ExpectedInterpretation">Interpretation later APIs and browser surfaces must preserve.</param>
/// <param name="Result">Sanitized result content for completed or partial scenarios.</param>
public sealed record SupportDiagnosticScenario(
    string Id,
    Guid SessionId,
    string DiagnosticMode,
    string? ProfileId,
    ScenarioClocks Clocks,
    string Outcome,
    string? RejectionDisposition,
    string FailureStage,
    string ReturnContext,
    IReadOnlyList<EvidenceClaim> Claims,
    string ExpectedInterpretation,
    SupportResultScenario? Result);
