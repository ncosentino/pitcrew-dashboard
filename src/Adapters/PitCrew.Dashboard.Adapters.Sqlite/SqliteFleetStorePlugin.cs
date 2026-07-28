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
    options.Services.AddSingleton<SqliteCapacityCommandStore>();
    options.Services.AddSingleton<ICapacityCommandStore>(
        static services =>
            services.GetRequiredService<SqliteCapacityCommandStore>());
    options.Services.AddSingleton<SqliteRecoveryCommandStore>();
    options.Services.AddSingleton<IRecoveryCommandStore>(
        static services =>
            services.GetRequiredService<SqliteRecoveryCommandStore>());
    options.Services.AddSingleton<SqliteFleetHistoryStore>();
    options.Services.AddSingleton<IFleetHistoryStore>(
        static services =>
            services.GetRequiredService<SqliteFleetHistoryStore>());
    options.Services.AddSingleton<SqliteAlertEvidenceStore>();
    options.Services.AddSingleton<IAlertEvidenceStore>(
        static services =>
            services.GetRequiredService<SqliteAlertEvidenceStore>());
    options.Services.AddSingleton<SqliteAlertIncidentStore>();
    options.Services.AddSingleton<IAlertIncidentStore>(
        static services =>
            services.GetRequiredService<SqliteAlertIncidentStore>());
    options.Services.AddSingleton<SqliteFleetTransactionFactory>();
    options.Services.AddSingleton<IFleetStorageTransactionFactory>(
        static services =>
            services.GetRequiredService<SqliteFleetTransactionFactory>());
    options.Services.AddSingleton<SqliteAccessStore>();
    options.Services.AddSingleton<IAccessStore>(
        static services => services.GetRequiredService<SqliteAccessStore>());
    options.Services.AddHostedService<SqliteFleetStoreInitializer>();
  }
}
