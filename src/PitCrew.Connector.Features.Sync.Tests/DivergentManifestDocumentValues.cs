namespace PitCrew.Connector.Features.Sync.Tests;

/// <summary>
/// Optional overrides used to inject values into
/// <c>manifest.document</c> that differ from the applied
/// <c>configuration</c>, so tests can prove the reconstructed manifest
/// derives from configuration (not from the stale source document).
/// </summary>
/// <remarks>
/// Only overrides for schema-allowed manifest properties are exposed:
/// labels, replicas, verificationCommands, and description. Configuration-
/// only projection fields (resources/runtime/serviceNetwork/readOnlyVolumes/
/// autoscaling/runnerGroup/disableDefaultLabels) live only in the applied
/// static configuration.
/// </remarks>
internal sealed record DivergentManifestDocumentValues(
    object[]? Labels = null,
    int? Replicas = null,
    string? Description = null,
    object? VerificationCommands = null);
