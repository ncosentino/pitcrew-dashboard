using Microsoft.AspNetCore.Builder;

using NexusLabs.Needlr.AspNet;

namespace PitCrew.Dashboard.Kernel.ExceptionHandling;

internal sealed class ExceptionHandlingWebPlugin : IWebApplicationPlugin
{
  public void Configure(WebApplicationPluginOptions options)
  {
    options.WebApplication.UseExceptionHandler();
  }
}
