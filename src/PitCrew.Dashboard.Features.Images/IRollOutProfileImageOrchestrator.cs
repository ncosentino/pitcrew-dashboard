using System.Security.Claims;

namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Loads one tenant-owned ready registry candidate, then hands off to the
/// fleet rollout queue.
/// </summary>
internal interface IRollOutProfileImageOrchestrator
{
  Task<RollOutProfileImageOutcome> QueueAsync(
      ClaimsPrincipal principal,
      string tenantId,
      RollOutProfileImageInput input,
      CancellationToken cancellationToken);
}
