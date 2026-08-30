namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Fences a campaign state mutation against one observed revision and target set.
/// </summary>
/// <param name="ExpectedRevision">Campaign revision observed by the caller.</param>
/// <param name="ExpectedTargetSetHash">Frozen target-set hash observed by the caller.</param>
public sealed record ImageRolloutCampaignMutationFence(
    int ExpectedRevision,
    string ExpectedTargetSetHash);
