namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Identifies one frozen campaign target's planning or execution state.
/// </summary>
public enum ImageRolloutCampaignTargetStatus
{
  /// <summary>
  /// The target passed planning and has not been dispatched.
  /// </summary>
  Eligible,

  /// <summary>
  /// The target was retained with a bounded exclusion reason.
  /// </summary>
  Excluded,

  /// <summary>
  /// The approved target is waiting for or has a queued profile command.
  /// </summary>
  Queued,

  /// <summary>
  /// The connector claimed the linked profile command.
  /// </summary>
  Claimed,

  /// <summary>
  /// The connector started the linked profile command.
  /// </summary>
  Applying,

  /// <summary>
  /// The image applied but stale workers remain or convergence is not yet proven.
  /// </summary>
  Rolling,

  /// <summary>
  /// The target digest and worker revision are current with zero stale workers.
  /// </summary>
  Complete,

  /// <summary>
  /// The linked profile command failed.
  /// </summary>
  Failed,

  /// <summary>
  /// Planning or command policy prevented the target from proceeding.
  /// </summary>
  Blocked,

  /// <summary>
  /// A started target has no provable terminal outcome.
  /// </summary>
  Indeterminate,

  /// <summary>
  /// The campaign was cancelled before this target received a profile command.
  /// </summary>
  Cancelled,
}
