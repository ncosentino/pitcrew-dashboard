namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Records one durable local image-rollout attempt.
/// </summary>
/// <param name="CommandId">Dashboard-assigned at-most-once command identifier.</param>
/// <param name="ProfileId">Locally resolved profile identifier.</param>
/// <param name="CandidateId">Approved candidate identity.</param>
/// <param name="RecipeId">Locally allowed recipe identifier.</param>
/// <param name="TargetDigest">Immutable candidate digest applied to the reconstructed manifest.</param>
/// <param name="TargetPlatform">Closed candidate platform.</param>
/// <param name="RegistryRepository">Locally configured registry repository for the recipe.</param>
/// <param name="LocalManifestPath">Connector-generated local manifest path.</param>
/// <param name="ExpectedCurrentImageReference">Expected current image reference before rollout.</param>
/// <param name="ExpectedCurrentImageDigest">Expected current image digest before rollout.</param>
/// <param name="ExpectedCurrentLocalImageId">Expected current local image identity before rollout.</param>
/// <param name="ExpectedCurrentWorkerRevision">Expected current worker revision before rollout.</param>
/// <param name="ExpectedStaticFingerprint">Expected static fingerprint before rollout.</param>
/// <param name="ExpectedPreservedConfigurationFingerprint">Expected preserved-configuration fingerprint before rollout.</param>
/// <param name="ExpectedRoutingFingerprint">Expected routing fingerprint before rollout.</param>
/// <param name="ExpectedDesiredGeneration">Expected desired-capacity generation before rollout.</param>
/// <param name="ExpectedDesiredStateHash">Expected desired-state hash before rollout.</param>
/// <param name="ResolvedPreOperationRevision">Locally resolved current worker revision immediately before starting.</param>
/// <param name="ManifestSourcePath">Current static-profile manifest source path recorded before starting (for retention protection).</param>
/// <param name="StartedAt">Connector time when the attempt was durably recorded.</param>
/// <param name="Phase">Durable phase: started or terminal.</param>
/// <param name="Status">Terminal status when the attempt is resolved; otherwise <see langword="null"/>.</param>
/// <param name="FailureCategory">Bounded non-success category, or <see langword="null"/>.</param>
/// <param name="Message">Bounded operator-facing result detail.</param>
/// <param name="TargetWorkerRevision">Worker revision observed after execution.</param>
/// <param name="ManagerConvergenceStatus">Convergence status observed after execution.</param>
/// <param name="CurrentWorkers">Live workers on the target revision, or <see langword="null"/>.</param>
/// <param name="StaleWorkers">Live workers on prior revision, or <see langword="null"/>.</param>
/// <param name="LastError">Bounded most recent rollout error, or <see langword="null"/>.</param>
/// <param name="CompletedAt">Connector time when the attempt reached a terminal state.</param>
internal sealed record ImageRolloutLedgerEntry(
    Guid CommandId,
    string ProfileId,
    Guid CandidateId,
    string RecipeId,
    string TargetDigest,
    string TargetPlatform,
    string RegistryRepository,
    string LocalManifestPath,
    string? ExpectedCurrentImageReference,
    string? ExpectedCurrentImageDigest,
    string? ExpectedCurrentLocalImageId,
    string? ExpectedCurrentWorkerRevision,
    string ExpectedStaticFingerprint,
    string ExpectedPreservedConfigurationFingerprint,
    string ExpectedRoutingFingerprint,
    int ExpectedDesiredGeneration,
    string? ExpectedDesiredStateHash,
    string? ResolvedPreOperationRevision,
    string? ManifestSourcePath,
    DateTimeOffset StartedAt,
    string Phase,
    string? Status,
    string? FailureCategory,
    string? Message,
    string? TargetWorkerRevision,
    string? ManagerConvergenceStatus,
    int? CurrentWorkers,
    int? StaleWorkers,
    string? LastError,
    DateTimeOffset? CompletedAt);
