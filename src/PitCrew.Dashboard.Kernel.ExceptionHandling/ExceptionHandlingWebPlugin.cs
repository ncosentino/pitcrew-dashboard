using Microsoft.AspNetCore.Builder;

using NexusLabs.Needlr;
using NexusLabs.Needlr.AspNet;

namespace PitCrew.Dashboard.Kernel.ExceptionHandling;

[PluginOrder(-300)]
internal sealed class ExceptionHandlingWebPlugin : IWebApplicationPlugin
{
  public void Configure(WebApplicationPluginOptions options)
  {
    options.WebApplication.UseExceptionHandler();
  }
}
