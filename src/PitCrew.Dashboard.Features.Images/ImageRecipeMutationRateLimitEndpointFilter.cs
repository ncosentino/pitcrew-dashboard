using System.Globalization;

using Microsoft.AspNetCore.Http;

namespace PitCrew.Dashboard.Features.Images;

internal sealed class ImageRecipeMutationRateLimitEndpointFilter(
    ImageRecipeMutationLimiter _limiter,
    TimeProvider _timeProvider) : IEndpointFilter
{
  public ValueTask<object?> InvokeAsync(
      EndpointFilterInvocationContext context,
      EndpointFilterDelegate next)
  {
    var tenantId = Convert.ToString(
        context.HttpContext.Request.RouteValues["tenantId"],
        CultureInfo.InvariantCulture);
    if (string.IsNullOrWhiteSpace(tenantId))
    {
      return new ValueTask<object?>(Results.BadRequest(new
      {
        error = new
        {
          code = "invalid_image_recipe_tenant",
          message = "Tenant ID route value is missing or invalid.",
        },
      }));
    }

    if (!_limiter.Acquire(
            tenantId,
            out var retryAt))
    {
      if (retryAt is not null)
      {
        var delay = retryAt.Value - _timeProvider.GetUtcNow();
        context.HttpContext.Response.Headers["Retry-After"] = Math.Max(
            0,
            (int)Math.Ceiling(delay.TotalSeconds)).ToString(
                CultureInfo.InvariantCulture);
      }

      return new ValueTask<object?>(Results.Json(
          new
          {
            error = new
            {
              code = "image_recipe_mutation_rate_limited",
              message = "Image recipe mutations are temporarily rate-limited for the tenant.",
            },
          },
          statusCode: StatusCodes.Status429TooManyRequests));
    }

    return next(context);
  }
}
