namespace PitCrew.Dashboard.Features.Images;

internal enum ImageRolloutCampaignCommandStatus
{
  Created,
  Updated,
  IdempotentReplay,
  Forbidden,
  NotFound,
  Invalid,
  Conflict,
  TargetLimitExceeded,
  RollbackAuthorityUnavailable,
}
