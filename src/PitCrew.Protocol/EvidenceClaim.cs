namespace PitCrew.Protocol;

/// <summary>
/// Projects one bounded operational fact with its authority, clocks, coverage, and retention.
/// </summary>
/// <param name="Name">Stable claim identifier.</param>
/// <param name="Authority">Source family allowed to assert the claim.</param>
/// <param name="Source">Bounded evidence source within the authority family.</param>
/// <param name="SourceIdentity">Stable authoritative observer identity when available.</param>
/// <param name="SourceObservedAt">Time the authority observed or produced the fact.</param>
/// <param name="DashboardReceivedAt">Time Dashboard durably accepted the evidence.</param>
/// <param name="EvaluatedAt">Time Dashboard evaluated freshness or policy.</param>
/// <param name="VerifiedAt">Time Dashboard authenticated and validated evidence when required.</param>
/// <param name="ResponseGeneratedAt">Time the API projection was assembled.</param>
/// <param name="FreshnessBoundary">Exclusive instant at which present-state evidence becomes stale.</param>
/// <param name="Coverage">Required-source coverage: complete, partial, or unavailable.</param>
/// <param name="Retention">Whether the value is live or retained as last-known evidence.</param>
/// <param name="Freshness">Current semantic state for the claim.</param>
/// <param name="Value">Bounded claim value, preserving explicit zero when measured.</param>
/// <param name="UnavailableReason">Closed reason when evidence is partial or unavailable.</param>
public sealed record EvidenceClaim(
    string Name,
    string Authority,
    string Source,
    string? SourceIdentity,
    DateTimeOffset? SourceObservedAt,
    DateTimeOffset? DashboardReceivedAt,
    DateTimeOffset? EvaluatedAt,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset ResponseGeneratedAt,
    DateTimeOffset? FreshnessBoundary,
    string Coverage,
    string Retention,
    string Freshness,
    string? Value,
    string? UnavailableReason);
