using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one profile whose current worker image may be replaced with one
/// approved immutable candidate through an outbound connector.
/// </summary>
/// <param name="ProfileId">Locally resolved profile identifier.</param>
/// <param name="Architecture">Locally advertised Linux architecture: <c>linux/amd64</c> or <c>linux/arm64</c>.</param>
/// <param name="CurrentImageReference">Currently configured image reference, when reported.</param>
/// <param name="CurrentImageDigest">Currently applied immutable digest, when known locally.</param>
/// <param name="CurrentLocalImageId">Currently observed local Docker image identity, when known.</param>
/// <param name="CurrentWorkerRevision">Currently applied worker revision from local static state.</param>
/// <param name="StaticFingerprint">Static profile fingerprint sourced from local applied state.</param>
/// <param name="PreservedConfigurationFingerprint">Connector-computed fingerprint of every non-image worker configuration field.</param>
/// <param name="RoutingFingerprint">Connector-computed fingerprint of routing, scope, targets, capacity, and pause.</param>
/// <param name="DesiredGeneration">Current desired-capacity generation.</param>
/// <param name="DesiredStateHash">Desired-state hash currently acknowledged, when present.</param>
/// <param name="AllowedRecipeIds">Locally allowed recipe identifiers.</param>
/// <param name="RolloutAllowed">Whether local policy allows rollout for the profile.</param>
/// <param name="LocalSchemaSupported">Whether the locally installed manager and static schema is supported by the projection.</param>
/// <param name="LocalFailureCategory">Bounded reason rollout is unsupported, or <see langword="null"/>.</param>
/// <param name="OperationActive">Whether another local profile operation is already active.</param>
/// <param name="ObservedStateAgeSeconds">Age of the locally readable observed state.</param>
/// <param name="CommandTimeoutSeconds">Bounded local execution timeout.</param>
/// <param name="MaximumExpirySeconds">Longest command lifetime the connector accepts.</param>
/// <param name="ManagerConvergenceStatus">Manager convergence status: <c>current</c>, <c>rolling</c>, or <c>degraded</c>.</param>
/// <param name="CurrentWorkers">Live workers currently using the applied revision, or <see langword="null"/> when unavailable.</param>
/// <param name="StaleWorkers">Live workers retained on an older revision, or <see langword="null"/> when unavailable.</param>
public sealed record ImageRolloutOperatorProfile(
    [property: JsonRequired] string ProfileId,
    [property: JsonRequired] string Architecture,
    [property: JsonRequired] string? CurrentImageReference,
    [property: JsonRequired] string? CurrentImageDigest,
    [property: JsonRequired] string? CurrentLocalImageId,
    [property: JsonRequired] string? CurrentWorkerRevision,
    [property: JsonRequired] string StaticFingerprint,
    [property: JsonRequired] string PreservedConfigurationFingerprint,
    [property: JsonRequired] string RoutingFingerprint,
    [property: JsonRequired] int DesiredGeneration,
    [property: JsonRequired] string? DesiredStateHash,
    [property: JsonRequired] IReadOnlyList<string> AllowedRecipeIds,
    [property: JsonRequired] bool RolloutAllowed,
    [property: JsonRequired] bool LocalSchemaSupported,
    [property: JsonRequired] string? LocalFailureCategory,
    [property: JsonRequired] bool OperationActive,
    [property: JsonRequired] int ObservedStateAgeSeconds,
    [property: JsonRequired] int CommandTimeoutSeconds,
    [property: JsonRequired] int MaximumExpirySeconds,
    [property: JsonRequired] string ManagerConvergenceStatus,
    [property: JsonRequired] int? CurrentWorkers,
    [property: JsonRequired] int? StaleWorkers);
