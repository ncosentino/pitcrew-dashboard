namespace PitCrew.Support.Canary.Contracts;

/// <summary>
/// Requests one closed run-scoped rejected-request control operation.
/// </summary>
/// <param name="SchemaVersion">Control contract schema version.</param>
/// <param name="RunId">Canary run identifier.</param>
/// <param name="RequestId">Unique control request identifier.</param>
/// <param name="Operation">Enqueue or cancel operation.</param>
/// <param name="InjectionCase">Closed request shape for enqueue.</param>
/// <param name="SessionId">Relay session to enqueue or cancel.</param>
/// <param name="NodeId">Enrolled node route for enqueue.</param>
/// <param name="NodeEncryptionPublicKeySpki">
/// Enrolled node RSA public key for enqueue.
/// </param>
/// <param name="ReplayId">
/// Shared replay group for the seed and repeated request.
/// </param>
public sealed record CanaryRejectedRequestControlRequest(
    int SchemaVersion,
    string RunId,
    Guid RequestId,
    string Operation,
    string? InjectionCase,
    Guid SessionId,
    Guid? NodeId,
    string? NodeEncryptionPublicKeySpki,
    Guid? ReplayId);
