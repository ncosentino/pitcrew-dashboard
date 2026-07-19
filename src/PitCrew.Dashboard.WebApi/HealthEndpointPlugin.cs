using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using NexusLabs.Needlr;
using NexusLabs.Needlr.AspNet;

namespace PitCrew.Dashboard.WebApi;

internal sealed class HealthEndpointPlugin :
    IServiceCollectionPlugin,
    IWebApplicationPlugin
{
  public void Configure(ServiceCollectionPluginOptions options) =>
      options.Services.AddHealthChecks();

  public void Configure(WebApplicationPluginOptions options) =>
      options.WebApplication.MapHealthChecks("/health");
}
