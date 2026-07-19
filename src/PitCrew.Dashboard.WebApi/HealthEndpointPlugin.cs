using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using NexusLabs.Needlr;
using NexusLabs.Needlr.AspNet;

namespace PitCrew.Dashboard.WebApi;

internal sealed class HealthEndpointPlugin :
    IServiceCollectionPlugin,
    IWebApplicationPlugin
{
  private const string HostedIngressContract =
      "pitcrew-dashboard-hosted-ingress-v1";

  public void Configure(ServiceCollectionPluginOptions options) =>
      options.Services.AddHealthChecks();

  public void Configure(WebApplicationPluginOptions options)
  {
    options.WebApplication.MapHealthChecks("/health");
    options.WebApplication.MapGet(
        "/health/hosted-ingress/v1",
        static () => Results.Text(
            HostedIngressContract,
            "text/plain"));
  }
}
