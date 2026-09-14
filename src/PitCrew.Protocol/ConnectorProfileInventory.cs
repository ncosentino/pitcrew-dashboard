namespace PitCrew.Protocol;

/// <summary>
/// Describes connector protocol 12 profile inventory acquisition.
/// </summary>
/// <param name="Coverage">Complete, partial, or unavailable inventory coverage.</param>
/// <param name="ObservedAt">Time the connector completed the local inventory attempt.</param>
/// <param name="UnavailableReason">Closed connector acquisition reason when coverage is not complete.</param>
public sealed record ConnectorProfileInventory(
    string Coverage,
    DateTimeOffset ObservedAt,
    string? UnavailableReason);
