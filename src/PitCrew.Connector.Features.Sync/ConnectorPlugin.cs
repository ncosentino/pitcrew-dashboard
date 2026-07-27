using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NexusLabs.Needlr;

namespace PitCrew.Connector.Features.Sync;

internal sealed class ConnectorPlugin : IServiceCollectionPlugin
{
  public void Configure(ServiceCollectionPluginOptions options)
  {
    if (OperatingSystem.IsWindows())
    {
      options.Services.AddWindowsService(service =>
      {
        service.ServiceName = "PitCrewConnector";
      });
    }
    options.Services
        .AddOptions<ConnectorOptions>()
        .BindConfiguration("PitCrew:Connector");
    options.Services.AddSingleton(TimeProvider.System);
    options.Services.AddSingleton<IHostExecutionEnvironment, HostExecutionEnvironment>();
    options.Services.AddSingleton<ISetupProcessRunner, SetupProcessRunner>();
    options.Services.AddSingleton<LocalProfileStateLocator>();
    options.Services.AddSingleton<LocalProfileOperationGate>();
    options.Services.AddSingleton<CapacityCommandExecutor>();
    options.Services.AddSingleton<RecoveryProfileResolver>();
    options.Services.AddSingleton<RecoveryCommandLedger>();
    options.Services.AddSingleton<RecoveryCommandExecutor>();
    options.Services.AddHttpClient<ConnectorApiClient>(
        static (services, client) =>
        {
          var connectorOptions = services
                  .GetRequiredService<IOptions<ConnectorOptions>>()
                  .Value;
          client.BaseAddress = new Uri(
                  connectorOptions.DashboardUrl,
                  UriKind.Absolute);
          client.Timeout = TimeSpan.FromSeconds(30);
        });
    options.Services.AddHostedService<ConnectorWorker>();
  }
}
