namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Identifies the durable lifecycle state of one image rollout campaign.
/// </summary>
public enum ImageRolloutCampaignStatus
{
  /// <summary>
  /// The frozen target set exists but canary and wave configuration is not final.
  /// </summary>
  Draft,

  /// <summary>
  /// The next wave requires explicit administrator approval.
  /// </summary>
  AwaitingApproval,

  /// <summary>
  /// An approved wave has dispatchable or active targets.
  /// </summary>
  Running,

  /// <summary>
  /// Future target dispatch is paused while existing commands continue.
  /// </summary>
  Paused,

  /// <summary>
  /// Every eligible target proved full convergence.
  /// </summary>
  Complete,

  /// <summary>
  /// At least one target completed and at least one ended adversely.
  /// </summary>
  Partial,

  /// <summary>
  /// No target completed and the campaign cannot safely progress.
  /// </summary>
  Blocked,

  /// <summary>
  /// Future target dispatch was explicitly cancelled.
  /// </summary>
  Cancelled,
}
