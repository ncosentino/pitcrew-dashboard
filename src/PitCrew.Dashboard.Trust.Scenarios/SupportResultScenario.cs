namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Carries sanitized support result content that can be persisted and read exactly.
/// </summary>
/// <param name="ReportJson">Bounded structured diagnostic report JSON.</param>
/// <param name="Markdown">Bounded human-readable diagnostic summary.</param>
public sealed record SupportResultScenario(
    string ReportJson,
    string Markdown);
