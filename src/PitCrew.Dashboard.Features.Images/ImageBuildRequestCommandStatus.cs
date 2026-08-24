namespace PitCrew.Dashboard.Features.Images;

internal enum ImageBuildRequestCommandStatus
{
  Succeeded,
  Unchanged,
  Invalid,
  Conflict,
  NotFound,
  Forbidden,
  NotConfigured,
  RateLimited,
  Unavailable,
}
