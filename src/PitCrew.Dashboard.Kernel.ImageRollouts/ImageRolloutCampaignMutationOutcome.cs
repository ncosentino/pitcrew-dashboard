namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Identifies the result of one campaign mutation.
/// </summary>
public enum ImageRolloutCampaignMutationOutcome
{
  /// <summary>
  /// The mutation changed durable state.
  /// </summary>
  Succeeded,

  /// <summary>
  /// An identical idempotent request already produced the returned campaign.
  /// </summary>
  IdempotentReplay,

  /// <summary>
  /// The idempotency key was reused with different authority.
  /// </summary>
  IdempotencyKeyReuseConflict,

  /// <summary>
  /// The campaign does not exist in the requested tenant.
  /// </summary>
  NotFound,

  /// <summary>
  /// The campaign lifecycle does not permit the requested mutation.
  /// </summary>
  InvalidState,

  /// <summary>
  /// The supplied campaign revision or target-set hash is stale.
  /// </summary>
  StaleFence,

  /// <summary>
  /// The requested canary is not an eligible frozen target.
  /// </summary>
  InvalidCanary,

  /// <summary>
  /// The fleet inventory exceeds the configured hard campaign target ceiling.
  /// </summary>
  TargetLimitExceeded,

  /// <summary>
  /// No source target has enough prior authority for a rollback campaign.
  /// </summary>
  RollbackAuthorityUnavailable,
}
