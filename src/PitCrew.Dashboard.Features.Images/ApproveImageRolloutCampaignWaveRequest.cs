namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Approves one pending campaign wave against current campaign fences.
/// </summary>
/// <param name="ExpectedRevision">Campaign revision observed by the caller.</param>
/// <param name="ExpectedTargetSetHash">Frozen target-set hash observed by the caller.</param>
public sealed record ApproveImageRolloutCampaignWaveRequest(
    int ExpectedRevision,
    string ExpectedTargetSetHash);
