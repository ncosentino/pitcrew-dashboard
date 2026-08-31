namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Describes one immutable target snapshot captured while creating a campaign.
/// </summary>
/// <param name="TargetId">Dashboard-assigned target identifier.</param>
/// <param name="NodeId">Dashboard node identifier.</param>
/// <param name="NodeDisplayName">Operator-facing node name captured with the plan.</param>
/// <param name="ProfileId">PitCrew profile identifier.</param>
/// <param name="Candidate">Per-target image authority when eligible.</param>
/// <param name="Fences">Exact current profile fences when eligible.</param>
/// <param name="ExclusionCategory">Closed exclusion category, or <see langword="null"/> when eligible.</param>
public sealed record ImageRolloutCampaignPlannedTarget(
    Guid TargetId,
    Guid NodeId,
    string NodeDisplayName,
    string ProfileId,
    ImageRolloutCandidateAuthority? Candidate,
    ImageRolloutCommandFences? Fences,
    string? ExclusionCategory);
