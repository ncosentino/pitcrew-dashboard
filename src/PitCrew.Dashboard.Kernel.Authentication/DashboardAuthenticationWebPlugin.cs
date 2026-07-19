using Microsoft.AspNetCore.Builder;

using NexusLabs.Needlr;
using NexusLabs.Needlr.AspNet;

namespace PitCrew.Dashboard.Kernel.Authentication;

[PluginOrder(-100)]
internal sealed class DashboardAuthenticationWebPlugin :
    IWebApplicationPlugin
{
  public void Configure(WebApplicationPluginOptions options)
  {
    options.WebApplication.UseRouting();
    options.WebApplication.UseAuthentication();
    options.WebApplication.UseAuthorization();
  }
}
