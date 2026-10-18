using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NexusLabs.Needlr;

namespace PitCrew.Connector.Features.Sync;

internal sealed class ConnectorPlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    // Needlr's generic-host bootstrap does not invoke generated options registrars.
    Generated.TypeRegistry.RegisterOptions(options.Services, options.Config);
    if (OperatingSystem.IsWindows())
    {
      options.Services.AddWindowsService(service =>
      {
        service.ServiceName = "PitCrewConnector";
      });
    }
    options.Services.AddSingleton(TimeProvider.System);
    options.Services.AddSingleton<IHostExecutionEnvironment, HostExecutionEnvironment>();
    options.Services.AddSingleton<ISetupProcessRunner, SetupProcessRunner>();
    options.Services.AddSingleton<LocalProfileStateLocator>();
    options.Services.AddSingleton<LocalProfileOperationGate>();
    options.Services.AddSingleton<CapacityCommandExecutor>();
    options.Services.AddSingleton<RecoveryProfileResolver>();
    options.Services.AddSingleton<RecoveryCommandLedger>();
    options.Services.AddSingleton<RecoveryCommandExecutor>();
    options.Services.AddSingleton<ConnectorHealthJournal>();
    options.Services.AddSingleton<ConnectorHealthReplayStore>();
    options.Services.AddHostedService<ConnectorWorker>();
  }
}
