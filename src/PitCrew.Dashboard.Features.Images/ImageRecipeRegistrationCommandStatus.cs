namespace PitCrew.Dashboard.Features.Images;

internal enum ImageRecipeRegistrationCommandStatus
{
  Succeeded,
  Unchanged,
  Invalid,
  Conflict,
  NotFound,
  NotConfigured,
  RateLimited,
  Unavailable,
  Forbidden,
}
