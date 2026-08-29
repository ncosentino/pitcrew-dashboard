namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Describes the immutable audit and lifecycle record of one profile-image
/// rollout command.
/// </summary>
/// <remarks>
/// The <c>Previous*</c> fields are only populated when Dashboard can prove
/// them from a previously succeeded rollout for the same node and profile
/// whose applied digest matches the currently observed image digest. They
/// stay <see langword="null"/> for the first rollout, for unmanaged legacy
/// images, and whenever the currently applied digest cannot be tied back to
/// a specific prior success. They are never inferred.
/// </remarks>
public sealed record ImageRolloutCommandState(
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
