namespace PitCrew.Dashboard.Trust.Scenarios;

/// <summary>
/// Defines one incident cardinality and current bounded-query expectation.
/// </summary>
/// <param name="Id">Stable scenario identifier.</param>
/// <param name="IncidentCount">Number of independently decomposed incidents.</param>
/// <param name="QueryLimit">Current bounded query limit.</param>
/// <param name="ExpectedVisibleCount">Number of incidents visible in the first page.</param>
/// <param name="ExpectedTruncated">Whether the current page reports hidden records.</param>
/// <param name="AuthoritativeTotalAvailable">Whether the current response carries the authoritative total.</param>
public sealed record IncidentCardinalityScenario(
    string Id,
    int IncidentCount,
    int QueryLimit,
    int ExpectedVisibleCount,
    bool ExpectedTruncated,
    bool AuthoritativeTotalAvailable);
