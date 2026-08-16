using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

using PitCrew.Dashboard.Features.Access;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class SupportAntiforgeryEndpointFilter(
    IAntiforgery _antiforgery,
    IDiagnosticAccessScopeAccessor _diagnosticScopeAccessor) : IEndpointFilter
{
  public async ValueTask<object?> InvokeAsync(
      EndpointFilterInvocationContext invocationContext,
      EndpointFilterDelegate next)
  {
    if (_diagnosticScopeAccessor.GetOrNull(invocationContext.HttpContext.User) is not null)
    {
      return await next(invocationContext);
    }
    return await _antiforgery.IsRequestValidAsync(invocationContext.HttpContext)
        ? await next(invocationContext)
        : Results.BadRequest(new
        {
          error = new
          {
            code = "invalid_antiforgery_token",
            message = "The antiforgery token is missing or invalid.",
          },
        });
  }
}
