namespace PitCrew.Support.Canary.Contracts;

/// <summary>
/// Reports the bounded outcome of one rejected-request control operation.
/// </summary>
/// <param name="SchemaVersion">Control contract schema version.</param>
/// <param name="RunId">Canary run identifier.</param>
/// <param name="RequestId">Request identifier copied from the request.</param>
/// <param name="Status">Succeeded or failed.</param>
/// <param name="Disposition">Closed operation disposition.</param>
public sealed record CanaryRejectedRequestControlResult(
    int SchemaVersion,
    string RunId,
    Guid RequestId,
    string Status,
    string Disposition);
