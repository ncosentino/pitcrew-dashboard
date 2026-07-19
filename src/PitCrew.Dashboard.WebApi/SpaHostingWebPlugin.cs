using Microsoft.AspNetCore.Builder;

using NexusLabs.Needlr.AspNet;

namespace PitCrew.Dashboard.WebApi;

/// <summary>
/// Serves the bundled single-page app (the Vite build in <c>wwwroot/</c>) from the
/// same host as the Carter API: static assets plus a client-side-routing fallback.
/// </summary>
/// <remarks>
/// The fallback is registered as a terminal endpoint (pattern <c>{*path:nonfile}</c>,
/// order <c>int.MaxValue</c>, GET/HEAD), so it never shadows Carter endpoints or
/// <c>/health</c> — any more specific endpoint outranks it by ASP.NET endpoint-routing
/// priority, and the <c>:nonfile</c> constraint lets missing assets 404 instead of
/// silently returning <c>index.html</c>. This holds regardless of plugin execution order.
/// </remarks>
internal sealed class SpaHostingWebPlugin : IWebApplicationPlugin
{
  public void Configure(WebApplicationPluginOptions options)
  {
    options.WebApplication.UseStaticFiles();
    options.WebApplication.MapFallbackToFile("index.html");
  }
}
