using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using NexusLabs.Needlr;
using NexusLabs.Needlr.AspNet;

namespace PitCrew.Dashboard.Features.Access;

[PluginOrder(-110)]
internal sealed class DiagnosticAuthenticationThrottleWebPlugin :
    IWebApplicationPlugin
{
  public void Configure(WebApplicationPluginOptions options) =>
      options.WebApplication.UseMiddleware<
          DiagnosticAuthenticationThrottleMiddleware>();
}

internal sealed class DiagnosticAuthenticationThrottleMiddleware(
    RequestDelegate _next)
{
#pragma warning disable NLF0020 // Conventional ASP.NET middleware receives cancellation through HttpContext.RequestAborted.
  public async Task InvokeAsync(
      HttpContext context,
      DiagnosticRequestLimiter limiter)
  {
    if (context.Request.Path.StartsWithSegments(
            "/api/diagnostics",
        StringComparison.OrdinalIgnoreCase))
    {
      var identity =
          context.Connection.RemoteIpAddress?.ToString() ??
          "unavailable";
      if (!limiter.AllowNetwork(identity))
      {
        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Diagnostic authentication rate exceeded.")
            .ExecuteAsync(context);
        return;
      }
    }
    await _next(context);
  }
#pragma warning restore NLF0020
}
