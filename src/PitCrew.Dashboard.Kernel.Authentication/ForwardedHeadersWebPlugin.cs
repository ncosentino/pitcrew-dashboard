using Microsoft.AspNetCore.Builder;

using NexusLabs.Needlr;
using NexusLabs.Needlr.AspNet;

namespace PitCrew.Dashboard.Kernel.Authentication;

[PluginOrder(-200)]
internal sealed class ForwardedHeadersWebPlugin : IWebApplicationPlugin
{
  public void Configure(WebApplicationPluginOptions options) =>
      options.WebApplication.UseForwardedHeaders();
}
