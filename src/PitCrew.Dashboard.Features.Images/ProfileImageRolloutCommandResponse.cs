namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Reports one immutable profile-image rollout command state.
/// </summary>
/// <remarks>
/// The <c>Previous*</c> fields describe the exact prior managed candidate,
/// recipe, digest, and worker revision when the currently applied image was
/// produced by an earlier succeeded rollout on the same node and profile.
/// They stay <see langword="null"/> for the first rollout, for unmanaged
/// legacy images, and whenever the currently applied digest cannot be tied
/// back to a specific prior success.
/// </remarks>
public sealed record ProfileImageRolloutCommandResponse(
    Guid CommandId,
    Guid CandidateId,
    string RecipeId,
    string TargetDigest,
    string TargetPlatform,
    string? PreviousImageReference,
    string? PreviousImageDigest,
    string? PreviousWorkerRevision,
    string Status,
    string? FailureCategory,
    string RequestedByGitHubUserId,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? TargetWorkerRevision,
    string? ManagerConvergenceStatus,
    int? CurrentWorkers,
    int? StaleWorkers,
    string? LastError,
    string? ResultMessage,
    Guid? PreviousCandidateId,
    string? PreviousRecipeId);
