using Microsoft.Extensions.DependencyInjection;

using NexusLabs.Needlr;
using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Features.Images.Abstractions;
using PitCrew.Dashboard.Features.Support.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Adapters.Sqlite;

internal sealed class SqliteFleetStorePlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    options.Services.AddSingleton<SqliteMigrationRunner>();
    options.Services.AddSingleton<SqliteFleetStore>();
    options.Services.AddSingleton<IFleetStore>(
        static services => services.GetRequiredService<SqliteFleetStore>());
    options.Services.AddSingleton<SqliteConnectorHealthStore>();
    options.Services.AddSingleton<IConnectorHealthStore>(
        static services =>
            services.GetRequiredService<SqliteConnectorHealthStore>());
    options.Services.AddSingleton<SqliteCapacityCommandStore>();
    options.Services.AddSingleton<ICapacityCommandStore>(
        static services =>
            services.GetRequiredService<SqliteCapacityCommandStore>());
    options.Services.AddSingleton<SqliteRecoveryCommandStore>();
    options.Services.AddSingleton<IRecoveryCommandStore>(
        static services =>
            services.GetRequiredService<SqliteRecoveryCommandStore>());
    options.Services.AddSingleton<SqliteImageRolloutCommandStore>();
    options.Services.AddSingleton<IImageRolloutCommandStore>(
        static services =>
            services.GetRequiredService<SqliteImageRolloutCommandStore>());
    options.Services.AddSingleton<SqliteImageRolloutCampaignStore>();
    options.Services.AddSingleton<IImageRolloutCampaignStore>(
        static services =>
            services.GetRequiredService<SqliteImageRolloutCampaignStore>());
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
    options.Services.AddSingleton<SqliteDiagnosticCredentialStore>();
    options.Services.AddSingleton<IDiagnosticCredentialStore>(
        static services =>
            services.GetRequiredService<SqliteDiagnosticCredentialStore>());
    options.Services.AddSingleton<SqliteSupportStore>();
    options.Services.AddSingleton<ISupportStore>(
        static services => services.GetRequiredService<SqliteSupportStore>());
    options.Services.AddSingleton<SqliteImageCandidateStore>();
    options.Services.AddSingleton<IImageCandidateStore>(
        static services =>
            services.GetRequiredService<SqliteImageCandidateStore>());
    options.Services.AddHostedService<SqliteFleetStoreInitializer>();
  }
}
