namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Carries one caller-supplied profile-image rollout request. Every value
/// is validated by the orchestrator before candidate/store work runs, so
/// oversized or malformed inputs never reach the SQLite layer.
/// </summary>
internal sealed record RollOutProfileImageInput(
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
    string? ExpectedDesiredStateHash,
    string IdempotencyKey);
