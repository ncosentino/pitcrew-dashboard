using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NexusLabs.Needlr;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class SupportPlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    options.Services.TryAddSingleton(TimeProvider.System);
    options.Services.AddHostedService<SupportRelayCleanupWorker>();
  }
}
