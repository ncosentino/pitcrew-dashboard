using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace PitCrew.Dashboard.Features.Access;

internal sealed class DiagnosticRateLimitEndpointFilter(
    DiagnosticRequestLimiter _limiter) : IEndpointFilter
{
  public ValueTask<object?> InvokeAsync(
      EndpointFilterInvocationContext context,
      EndpointFilterDelegate next)
  {
    var credentialValue = context.HttpContext.User.FindFirstValue(
        ClaimTypes.NameIdentifier);
    if (!Guid.TryParseExact(
        credentialValue,
        "N",
        out var credentialId))
    {
      return new ValueTask<object?>(Results.Unauthorized());
    }
    if (!_limiter.AllowCredential(credentialId))
    {
      return new ValueTask<object?>(Results.Problem(
          statusCode: StatusCodes.Status429TooManyRequests,
          title: "Diagnostic request rate exceeded."));
    }
    return next(context);
  }
}
