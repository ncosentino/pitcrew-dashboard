namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Identifies the approval and execution state of one immutable campaign wave.
/// </summary>
public enum ImageRolloutCampaignWaveStatus
{
  /// <summary>
  /// The wave has not been approved.
  /// </summary>
  Pending,

  /// <summary>
  /// The wave is approved and has undispatched targets.
  /// </summary>
  Approved,

  /// <summary>
  /// At least one target in the wave has a durable profile command.
  /// </summary>
  Running,

  /// <summary>
  /// Every target in the wave completed.
  /// </summary>
  Complete,

  /// <summary>
  /// At least one target in the wave ended adversely.
  /// </summary>
  Blocked,

  /// <summary>
  /// The campaign was cancelled before every target was dispatched.
  /// </summary>
  Cancelled,
}
