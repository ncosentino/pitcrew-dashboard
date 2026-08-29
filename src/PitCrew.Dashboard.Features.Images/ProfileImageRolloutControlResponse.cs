namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Reports one profile's rollout control and bounded history in a form the
/// dashboard UX can render.
/// </summary>
public sealed record ProfileImageRolloutControlResponse(
    Guid NodeId,
    string ProfileId,
    string Architecture,
    string? CurrentImageReference,
    string? CurrentImageDigest,
    string? CurrentLocalImageId,
    string? CurrentWorkerRevision,
    string StaticFingerprint,
    string PreservedConfigurationFingerprint,
    string RoutingFingerprint,
    int DesiredGeneration,
    string? DesiredStateHash,
    IReadOnlyList<string> AllowedRecipeIds,
    bool RolloutAllowed,
    bool LocalSchemaSupported,
    string? LocalFailureCategory,
    bool OperationActive,
    int ObservedStateAgeSeconds,
    int ObservedStateMaximumAgeSeconds,
    bool ObservedStateFresh,
    string ManagerConvergenceStatus,
    int? CurrentWorkers,
    int? StaleWorkers,
    ProfileImageRolloutCommandResponse? LatestCommand,
    IReadOnlyList<ProfileImageRolloutCommandResponse> RecentCommands);
