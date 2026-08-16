using Microsoft.Extensions.Configuration;

using PitCrew.Support.Broker.App;

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();
var options = SupportBrokerOptions.FromConfiguration(configuration);
var broker = new SupportDiagnosticsBroker(options);
var server = new SupportBrokerPipeServer(options, broker);
if (args.Contains("--run-once", StringComparer.Ordinal))
{
  await server.RunOnceAsync(CancellationToken.None);
  return;
}
while (true)
{
  await server.RunOnceAsync(CancellationToken.None);
}
