namespace PitCrew.Support.Canary.Contracts;

/// <summary>
/// Requests one closed run-scoped topology control operation.
/// </summary>
/// <param name="SchemaVersion">Control contract schema version.</param>
/// <param name="RunId">Canary run identifier.</param>
/// <param name="RequestId">Unique request identifier.</param>
/// <param name="Operation">Closed topology control operation.</param>
public sealed record CanaryTopologyControlRequest(
    int SchemaVersion,
    string RunId,
    Guid RequestId,
    string Operation);
