using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace PitCrew.Dashboard.Features.Images;

internal static class ImageRecipeEndpointConventionExtensions
{
  public static RouteHandlerBuilder AddImageRecipeMutationRateLimit(
      this RouteHandlerBuilder builder) =>
      builder.AddEndpointFilter<ImageRecipeMutationRateLimitEndpointFilter>();
}
