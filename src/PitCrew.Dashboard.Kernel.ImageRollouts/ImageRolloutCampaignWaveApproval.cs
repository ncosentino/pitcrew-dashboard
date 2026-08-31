namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Approves one exact pending campaign wave.
/// </summary>
/// <param name="WaveNumber">Immutable wave number.</param>
/// <param name="ExpectedRevision">Campaign revision observed by the caller.</param>
/// <param name="ExpectedTargetSetHash">Frozen target-set hash observed by the caller.</param>
public sealed record ImageRolloutCampaignWaveApproval(
    int WaveNumber,
    int ExpectedRevision,
    string ExpectedTargetSetHash);
