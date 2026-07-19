using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NexusLabs.Needlr;

using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class FleetPlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    options.Services.TryAddSingleton(TimeProvider.System);
    options.Services.Configure<JsonOptions>(
        static jsonOptions =>
            jsonOptions.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                PitCrewProtocolJsonContext.Default));
  }
}
