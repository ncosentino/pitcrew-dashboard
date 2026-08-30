namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Describes one frozen campaign target and its current bounded execution evidence.
/// </summary>
/// <param name="TargetId">Dashboard-assigned frozen target identifier.</param>
/// <param name="NodeId">Dashboard node identifier.</param>
/// <param name="NodeDisplayName">Operator-facing node name captured with the plan.</param>
/// <param name="ProfileId">PitCrew profile identifier.</param>
/// <param name="Candidate">Per-target image authority when available.</param>
/// <param name="Fences">Exact profile fences captured during planning.</param>
/// <param name="ExclusionCategory">Frozen exclusion reason, or <see langword="null"/> when eligible.</param>
/// <param name="Status">Current target planning or execution state.</param>
/// <param name="WaveNumber">Immutable assigned wave, or <see langword="null"/> before configuration.</param>
/// <param name="IsCanary">Whether this target is the explicit or implicit canary.</param>
/// <param name="CommandId">Linked profile rollout command, when dispatched.</param>
/// <param name="FailureCategory">Bounded adverse category, when terminally adverse.</param>
/// <param name="ResultMessage">Bounded operator-facing result evidence.</param>
/// <param name="TargetWorkerRevision">Worker revision produced by the target command.</param>
/// <param name="ManagerConvergenceStatus">Current bounded manager convergence state.</param>
/// <param name="CurrentWorkers">Workers on the target revision, or <see langword="null"/> when unavailable.</param>
/// <param name="StaleWorkers">Workers remaining on a prior revision, or <see langword="null"/> when unavailable.</param>
/// <param name="ClaimedAt">Connector claim time, when reported.</param>
/// <param name="StartedAt">Connector start time, when reported.</param>
/// <param name="CompletedAt">Target terminal time, when terminal.</param>
/// <param name="PreviousCandidateId">Previously applied candidate identity when proven.</param>
/// <param name="PreviousRecipeId">Previously applied recipe identity when proven.</param>
/// <param name="PreviousImageReference">Previously configured image reference when proven.</param>
/// <param name="PreviousImageDigest">Previously applied immutable digest when proven.</param>
/// <param name="PreviousWorkerRevision">Previously applied worker revision when proven.</param>
public sealed record ImageRolloutCampaignTargetState(
    Guid TargetId,
    Guid NodeId,
    string NodeDisplayName,
    string ProfileId,
    ImageRolloutCandidateAuthority? Candidate,
    ImageRolloutCommandFences? Fences,
    string? ExclusionCategory,
    ImageRolloutCampaignTargetStatus Status,
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
