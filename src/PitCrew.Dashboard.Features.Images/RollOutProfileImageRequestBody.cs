namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Requests a profile image rollout with authoritative candidate identity
/// and every observed fence.
/// </summary>
public sealed record RollOutProfileImageRequestBody(
    Guid NodeId,
    string ProfileId,
    Guid CandidateId,
    string? ExpectedCurrentImageReference,
    string? ExpectedCurrentImageDigest,
    string? ExpectedCurrentLocalImageId,
    string? ExpectedCurrentWorkerRevision,
    string ExpectedStaticFingerprint,
    string ExpectedPreservedConfigurationFingerprint,
    string ExpectedRoutingFingerprint,
    int ExpectedDesiredGeneration,
    string? ExpectedDesiredStateHash);
