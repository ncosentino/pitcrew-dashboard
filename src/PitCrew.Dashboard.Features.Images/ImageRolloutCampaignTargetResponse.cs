namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns one frozen campaign target and bounded execution evidence.
/// </summary>
/// <param name="TargetId">Frozen target identifier.</param>
/// <param name="NodeId">Dashboard node identifier.</param>
/// <param name="NodeDisplayName">Operator-facing node name captured with the plan.</param>
/// <param name="ProfileId">PitCrew profile identifier.</param>
/// <param name="Candidate">Per-target image authority.</param>
/// <param name="ExpectedCurrentImageReference">Frozen current image reference.</param>
/// <param name="ExpectedCurrentImageDigest">Frozen current immutable digest.</param>
/// <param name="ExpectedCurrentLocalImageId">Frozen local image identity.</param>
/// <param name="ExpectedCurrentWorkerRevision">Frozen current worker revision.</param>
/// <param name="ExpectedStaticFingerprint">Frozen static profile fingerprint.</param>
/// <param name="ExpectedPreservedConfigurationFingerprint">Frozen non-image configuration fingerprint.</param>
/// <param name="ExpectedRoutingFingerprint">Frozen routing and capacity fingerprint.</param>
/// <param name="ExpectedDesiredGeneration">Frozen desired generation.</param>
/// <param name="ExpectedDesiredStateHash">Frozen desired-state hash.</param>
/// <param name="ExclusionCategory">Frozen exclusion reason.</param>
/// <param name="Status">Current target state.</param>
/// <param name="WaveNumber">Assigned wave.</param>
/// <param name="IsCanary">Whether this is the canary target.</param>
/// <param name="CommandId">Linked profile rollout command.</param>
/// <param name="FailureCategory">Bounded adverse category.</param>
/// <param name="ResultMessage">Bounded operator-facing result evidence.</param>
/// <param name="TargetWorkerRevision">Worker revision produced by the command.</param>
/// <param name="ManagerConvergenceStatus">Current manager convergence state.</param>
/// <param name="CurrentWorkers">Workers using the target revision.</param>
/// <param name="StaleWorkers">Workers remaining on a prior revision.</param>
/// <param name="ClaimedAt">Connector claim time.</param>
/// <param name="StartedAt">Connector start time.</param>
/// <param name="CompletedAt">Target terminal time.</param>
/// <param name="PreviousCandidateId">Previously applied candidate when proven.</param>
/// <param name="PreviousRecipeId">Previously applied recipe when proven.</param>
/// <param name="PreviousImageReference">Previously configured image reference when proven.</param>
/// <param name="PreviousImageDigest">Previously applied immutable digest when proven.</param>
/// <param name="PreviousWorkerRevision">Previously applied worker revision when proven.</param>
public sealed record ImageRolloutCampaignTargetResponse(
    Guid TargetId,
    Guid NodeId,
    string NodeDisplayName,
    string ProfileId,
    ImageRolloutCampaignCandidateResponse? Candidate,
    string? ExpectedCurrentImageReference,
    string? ExpectedCurrentImageDigest,
    string? ExpectedCurrentLocalImageId,
    string? ExpectedCurrentWorkerRevision,
    string? ExpectedStaticFingerprint,
    string? ExpectedPreservedConfigurationFingerprint,
    string? ExpectedRoutingFingerprint,
    int? ExpectedDesiredGeneration,
    string? ExpectedDesiredStateHash,
    string? ExclusionCategory,
    string Status,
    int? WaveNumber,
    bool IsCanary,
    Guid? CommandId,
    string? FailureCategory,
    string? ResultMessage,
    string? TargetWorkerRevision,
    string? ManagerConvergenceStatus,
    int? CurrentWorkers,
    int? StaleWorkers,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    Guid? PreviousCandidateId,
    string? PreviousRecipeId,
    string? PreviousImageReference,
    string? PreviousImageDigest,
    string? PreviousWorkerRevision);
