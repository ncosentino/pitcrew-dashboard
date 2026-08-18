using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PitCrew.Support.Broker.App;

var builder = Host.CreateApplicationBuilder(args);
var options = SupportBrokerOptions.FromConfiguration(builder.Configuration) ??
    throw new InvalidOperationException(
        "PitCrew support broker configuration is incomplete.");
var broker = new SupportDiagnosticsBroker(options);
var server = SupportBrokerServerFactory.Create(options, broker);
if (args.Contains("--run-once", StringComparer.Ordinal))
{
  using (server)
  {
    await server.RunOnceAsync(CancellationToken.None);
  }
  return;
}
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(broker);
builder.Services.AddSingleton<ISupportBrokerServer>(server);
builder.Services.AddHostedService<SupportBrokerWorker>();
builder.Services.AddWindowsService(service =>
{
  service.ServiceName = "PitCrewSupportBroker";
});
await builder.Build().RunAsync();
