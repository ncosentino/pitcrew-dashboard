namespace PitCrew.Protocol;

/// <summary>
/// Describes manager contract 21 provenance for one source family.
/// </summary>
/// <param name="Authority">Authoritative producer family.</param>
/// <param name="Source">Closed manager source family.</param>
/// <param name="SourceIdentity">Manager instance that produced the observation.</param>
/// <param name="ObservedAt">Source-family observation time when usable evidence exists.</param>
/// <param name="Coverage">Complete, partial, or unavailable source coverage.</param>
/// <param name="Retention">Live or last-known evidence retention.</param>
/// <param name="Reason">Closed source reason when evidence is not complete and live.</param>
public sealed record ManagerSourceObservation(
    string Authority,
    string Source,
    string SourceIdentity,
    DateTimeOffset? ObservedAt,
    string Coverage,
    string Retention,
    string? Reason);
