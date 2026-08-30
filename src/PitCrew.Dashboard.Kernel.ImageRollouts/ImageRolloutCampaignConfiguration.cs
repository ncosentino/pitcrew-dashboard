namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Selects the immutable canary and wave size for one frozen draft campaign.
/// </summary>
/// <param name="CanaryTargetId">Exact eligible canary, or <see langword="null"/> for an implicit single target.</param>
/// <param name="WaveSize">Maximum targets assigned to each later wave.</param>
/// <param name="ExpectedRevision">Campaign revision observed by the caller.</param>
/// <param name="ExpectedTargetSetHash">Frozen target-set hash observed by the caller.</param>
public sealed record ImageRolloutCampaignConfiguration(
    Guid? CanaryTargetId,
    int WaveSize,
    int ExpectedRevision,
    string ExpectedTargetSetHash)
{
  /// <summary>
  /// Gets the hard maximum number of targets in one later wave.
  /// </summary>
  public const int MaximumWaveSize = 100;
}
