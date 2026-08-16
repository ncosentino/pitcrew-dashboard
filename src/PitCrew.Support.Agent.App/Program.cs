using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PitCrew.Support.Agent.App;

var builder = Host.CreateApplicationBuilder(args);
var options = SupportAgentOptions.FromConfiguration(builder.Configuration) ??
    throw new InvalidOperationException("PitCrew support agent configuration is incomplete.");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<AgentReplayCache>(
    _ => new AgentReplayCache(options.ReplayRoot));
builder.Services.AddSingleton<ILocalDiagnosticsBroker>(
    _ => new NamedPipeDiagnosticsBroker(options.PipeName));
builder.Services.AddHttpClient(SupportRelayTransportHttpClientOptions.ClientName, client =>
{
  client.BaseAddress = options.RelayUrl;
  client.Timeout = TimeSpan.FromSeconds(30);
  client.DefaultRequestHeaders.UserAgent.ParseAdd("PitCrew-Support-Agent/1");
});
builder.Services.AddSingleton<SupportRelayTransportClient>(services =>
    new SupportRelayTransportClient(
        services.GetRequiredService<IHttpClientFactory>(),
        options.TransportCredential));
builder.Services.AddSingleton<SupportAgentRequestProcessor>();
builder.Services.AddHostedService<SupportAgentWorker>();
await builder.Build().RunAsync();



