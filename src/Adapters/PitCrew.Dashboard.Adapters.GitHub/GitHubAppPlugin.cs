using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NexusLabs.Needlr;

namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed class GitHubAppPlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options) =>
      options.Services.TryAddSingleton(TimeProvider.System);
}
