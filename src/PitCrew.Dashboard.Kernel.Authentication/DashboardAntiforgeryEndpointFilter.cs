using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace PitCrew.Dashboard.Kernel.Authentication;

/// <summary>
/// Validates the dashboard antiforgery cookie and request header before an authenticated mutation.
/// </summary>
public sealed class DashboardAntiforgeryEndpointFilter(
    IAntiforgery _antiforgery) : IEndpointFilter
{
  /// <summary>
  /// Rejects invalid antiforgery requests before invoking the endpoint handler.
  /// </summary>
  /// <param name="invocationContext">Current endpoint invocation context.</param>
  /// <param name="next">Next endpoint filter or route handler.</param>
  /// <returns>The route result or a structured bad-request result.</returns>
  public async ValueTask<object?> InvokeAsync(
      EndpointFilterInvocationContext invocationContext,
      EndpointFilterDelegate next)
  {
    if (!await _antiforgery.IsRequestValidAsync(
        invocationContext.HttpContext))
    {
      return Results.BadRequest(new
      {
        error = new
        {
          code = "invalid_antiforgery_token",
          message =
              "The antiforgery token is missing or invalid.",
        },
      });
    }
    return await next(invocationContext);
  }
}
