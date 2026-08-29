namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Carries the expected fences an administrator observed before requesting rollout.
/// </summary>
public sealed record ImageRolloutCommandFences(
    string? ExpectedCurrentImageReference,
    string? ExpectedCurrentImageDigest,
    string? ExpectedCurrentLocalImageId,
    string? ExpectedCurrentWorkerRevision,
    string ExpectedStaticFingerprint,
    string ExpectedPreservedConfigurationFingerprint,
    string ExpectedRoutingFingerprint,
    int ExpectedDesiredGeneration,
    string? ExpectedDesiredStateHash);
