namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Identifies the outcome of one orchestrator-level rollout attempt before
/// or after the fleet queue write. Each value maps to a stable HTTP status
/// and problem code in the Carter surface.
/// </summary>
internal enum RollOutProfileImageStatus
{
  Queued,
  IdempotentReplay,
  IdempotencyKeyReuseConflict,
  Unauthorized,
  Invalid,
  CandidateNotFound,
  CandidateFailed,
  CandidateNotRegistryReady,
  Unsupported,
  UnsupportedTopology,
  NotAllowed,
  RecipeNotAllowed,
  RegistryNotAllowed,
  ArchitectureMismatch,
  StaleFence,
  Conflict,
  RateLimited,
}
