namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Freezes canary and wave assignment for one campaign draft.
/// </summary>
/// <param name="CanaryTargetId">Exact eligible canary, or <see langword="null"/> for an implicit single target.</param>
/// <param name="WaveSize">Maximum targets in each later wave.</param>
/// <param name="ExpectedRevision">Campaign revision observed by the caller.</param>
/// <param name="ExpectedTargetSetHash">Frozen target-set hash observed by the caller.</param>
public sealed record ConfigureImageRolloutCampaignRequest(
    Guid? CanaryTargetId,
    int WaveSize,
    int ExpectedRevision,
    string ExpectedTargetSetHash);
