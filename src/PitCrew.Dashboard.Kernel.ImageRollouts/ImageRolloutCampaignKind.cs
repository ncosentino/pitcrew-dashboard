namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Identifies whether a campaign applies one shared candidate or restores
/// per-target prior image authority.
/// </summary>
public enum ImageRolloutCampaignKind
{
  /// <summary>
  /// Applies one ready candidate to every eligible target.
  /// </summary>
  Forward,

  /// <summary>
  /// Applies each target's separately proven prior image authority.
  /// </summary>
  Rollback,
}
