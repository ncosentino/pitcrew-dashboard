using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Requests replacement of one profile's current worker image with one
/// approved immutable candidate through an outbound connector.
/// </summary>
/// <param name="CommandId">Dashboard-assigned at-most-once command identifier.</param>
/// <param name="CandidateId">Approved candidate identifier from the trusted image plane.</param>
/// <param name="RecipeId">Recipe identifier from the immutable candidate; the connector maps this to its configured registry repository.</param>
/// <param name="ProfileId">Locally resolved profile identifier.</param>
/// <param name="TargetDigest">Immutable lowercase <c>sha256:</c>-prefixed image digest.</param>
/// <param name="TargetPlatform">Closed candidate platform: <c>linux/amd64</c> or <c>linux/arm64</c>.</param>
/// <param name="ExpectedCurrentImageReference">Image reference the dashboard observed before requesting rollout, or <see langword="null"/>.</param>
/// <param name="ExpectedCurrentImageDigest">Image digest the dashboard observed before requesting rollout, or <see langword="null"/>.</param>
/// <param name="ExpectedCurrentLocalImageId">Local image identity the dashboard observed, or <see langword="null"/>.</param>
/// <param name="ExpectedCurrentWorkerRevision">Applied worker revision the dashboard observed, or <see langword="null"/>.</param>
/// <param name="ExpectedStaticFingerprint">Static profile fingerprint the dashboard observed.</param>
/// <param name="ExpectedPreservedConfigurationFingerprint">Preserved-configuration fingerprint the dashboard observed.</param>
/// <param name="ExpectedRoutingFingerprint">Routing fingerprint the dashboard observed.</param>
/// <param name="ExpectedDesiredGeneration">Desired-capacity generation that must still be current.</param>
/// <param name="ExpectedDesiredStateHash">Desired-state hash that must still be current, or <see langword="null"/>.</param>
/// <param name="RequestedAt">Dashboard time when the command was queued.</param>
/// <param name="ExpiresAt">Time after which the connector must reject the command.</param>
public sealed record RollOutProfileImageCommand(
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] Guid CandidateId,
    [property: JsonRequired] string RecipeId,
    [property: JsonRequired] string ProfileId,
    [property: JsonRequired] string TargetDigest,
    [property: JsonRequired] string TargetPlatform,
    [property: JsonRequired] string? ExpectedCurrentImageReference,
    [property: JsonRequired] string? ExpectedCurrentImageDigest,
    [property: JsonRequired] string? ExpectedCurrentLocalImageId,
    [property: JsonRequired] string? ExpectedCurrentWorkerRevision,
    [property: JsonRequired] string ExpectedStaticFingerprint,
    [property: JsonRequired] string ExpectedPreservedConfigurationFingerprint,
    [property: JsonRequired] string ExpectedRoutingFingerprint,
    [property: JsonRequired] int ExpectedDesiredGeneration,
    [property: JsonRequired] string? ExpectedDesiredStateHash,
    [property: JsonRequired] DateTimeOffset RequestedAt,
    [property: JsonRequired] DateTimeOffset ExpiresAt);
