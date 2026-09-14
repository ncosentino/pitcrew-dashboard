namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Describes the authority and availability of one bounded scenario claim.
/// </summary>
/// <param name="Name">Stable claim name.</param>
/// <param name="State">Availability state: authoritative, partial, or unavailable.</param>
/// <param name="Authority">Source that is allowed to make the claim.</param>
/// <param name="Freshness">Freshness state: current, stale, retained, partial, or unavailable.</param>
/// <param name="Value">Sanitized bounded value, or <see langword="null"/> when unavailable.</param>
/// <param name="SourceObservedAt">Time the source observed this claim, when available.</param>
/// <param name="DashboardReceivedAt">Time Dashboard accepted this claim, when available.</param>
public sealed record EvidenceClaim(
    string Name,
    string State,
    string Authority,
    string Freshness,
    string? Value,
    DateTimeOffset? SourceObservedAt,
    DateTimeOffset? DashboardReceivedAt);
