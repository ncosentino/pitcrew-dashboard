using Microsoft.Extensions.DependencyInjection;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteFleetStorePlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    options.Services.AddSingleton<SqliteFleetStore>();
    options.Services.AddSingleton<IFleetStore>(
        static services => services.GetRequiredService<SqliteFleetStore>());
    options.Services.AddHostedService<SqliteFleetStoreInitializer>();
  }
}
