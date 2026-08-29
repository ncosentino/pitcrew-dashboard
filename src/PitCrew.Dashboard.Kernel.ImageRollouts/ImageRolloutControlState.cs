namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Describes the rollout control currently advertised for one profile.
/// </summary>
public sealed record ImageRolloutControlState(
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
    DateTimeOffset CapabilityObservedAt,
    int ObservedStateMaximumAgeSeconds,
    string ManagerConvergenceStatus,
    int? CurrentWorkers,
    int? StaleWorkers,
    ImageRolloutCommandState? LatestCommand,
    IReadOnlyList<ImageRolloutCommandState> RecentCommands);
