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
    options.Services.AddSingleton<ICapacityProcessRunner, CapacityProcessRunner>();
    options.Services.AddSingleton<CapacityCommandExecutor>();
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
