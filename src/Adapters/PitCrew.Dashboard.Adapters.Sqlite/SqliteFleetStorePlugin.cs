using Microsoft.Extensions.DependencyInjection;

using NexusLabs.Needlr;
using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteFleetStorePlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    options.Services.AddSingleton<SqliteMigrationRunner>();
    options.Services.AddSingleton<SqliteFleetStore>();
    options.Services.AddSingleton<IFleetStore>(
        static services => services.GetRequiredService<SqliteFleetStore>());
    options.Services.AddSingleton<SqliteAccessStore>();
    options.Services.AddSingleton<IAccessStore>(
        static services => services.GetRequiredService<SqliteAccessStore>());
    options.Services.AddHostedService<SqliteFleetStoreInitializer>();
  }
}
