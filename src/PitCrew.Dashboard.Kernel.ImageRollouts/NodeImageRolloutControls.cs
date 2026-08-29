namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Groups rollout controls by enrolled node.
/// </summary>
public sealed record NodeImageRolloutControls(
    Guid NodeId,
    IReadOnlyList<ImageRolloutControlState> Profiles);
