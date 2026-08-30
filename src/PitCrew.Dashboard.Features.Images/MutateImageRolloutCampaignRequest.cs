namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Fences pause, resume, or cancellation against current campaign authority.
/// </summary>
/// <param name="ExpectedRevision">Campaign revision observed by the caller.</param>
/// <param name="ExpectedTargetSetHash">Frozen target-set hash observed by the caller.</param>
public sealed record MutateImageRolloutCampaignRequest(
    int ExpectedRevision,
    string ExpectedTargetSetHash);
