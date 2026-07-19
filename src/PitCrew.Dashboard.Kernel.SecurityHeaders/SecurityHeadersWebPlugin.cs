using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

using NexusLabs.Needlr;
using NexusLabs.Needlr.AspNet;

namespace PitCrew.Dashboard.Kernel.SecurityHeaders;

[PluginOrder(-150)]
internal sealed class SecurityHeadersWebPlugin :
    IServiceCollectionPlugin,
    IWebApplicationPlugin
{
  private const string ContentSecurityPolicy =
      "default-src 'self'; base-uri 'self'; connect-src 'self'; form-action 'self'; frame-ancestors 'none'; img-src 'self' https://avatars.githubusercontent.com data:; object-src 'none'; script-src 'self'; style-src 'self'";

  public void Configure(ServiceCollectionPluginOptions options) =>
      options.Services.AddHsts(hstsOptions =>
      {
        hstsOptions.MaxAge = TimeSpan.FromDays(365);
        hstsOptions.IncludeSubDomains = false;
        hstsOptions.Preload = false;
      });

  public void Configure(WebApplicationPluginOptions options)
  {
    if (!options.WebApplication.Environment.IsDevelopment())
    {
      options.WebApplication.UseHsts();
    }
    options.WebApplication.Use(ApplyHeadersAsync);
  }

  private static Task ApplyHeadersAsync(
      HttpContext context,
      Func<Task> next)
  {
    context.Response.OnStarting(
        static state =>
        {
          var response = (HttpResponse)state;
          response.Headers[HeaderNames.ContentSecurityPolicy] =
              ContentSecurityPolicy;
          response.Headers[HeaderNames.XContentTypeOptions] =
              "nosniff";
          response.Headers[HeaderNames.XFrameOptions] = "DENY";
          response.Headers["Referrer-Policy"] = "no-referrer";
          response.Headers["Permissions-Policy"] =
              "camera=(), geolocation=(), microphone=()";
          return Task.CompletedTask;
        },
        context.Response);
    return next();
  }
}
