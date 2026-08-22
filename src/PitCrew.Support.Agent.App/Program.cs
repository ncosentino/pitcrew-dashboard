using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PitCrew.Support.Agent.App;

const string rotateCommand = "rotate";
const string deleteIdentityCommand = "identity-delete-keys";
var rotateMode = args.Contains(
    rotateCommand,
    StringComparer.OrdinalIgnoreCase);
var deleteIdentityMode = args.Contains(
    deleteIdentityCommand,
    StringComparer.OrdinalIgnoreCase);
if (rotateMode && deleteIdentityMode)
{
  throw new InvalidOperationException(
      "Only one support-agent command mode may run at a time.");
}
var hostArguments = args
    .Where(argument =>
        !string.Equals(
            argument,
            rotateCommand,
            StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(
            argument,
            deleteIdentityCommand,
            StringComparison.OrdinalIgnoreCase))
    .ToArray();
var builder = Host.CreateApplicationBuilder(hostArguments);
var bootstrapOptions =
    SupportAgentBootstrapOptions.FromConfiguration(builder.Configuration) ??
    throw new InvalidOperationException(
        "PitCrew support agent bootstrap configuration is invalid.");
var identityManager = SupportNodeIdentityManager.CreateDefault(
    bootstrapOptions.IdentityRoot);
builder.Services.AddSingleton(bootstrapOptions);
builder.Services.AddSingleton(identityManager);
builder.Services.AddSingleton(identityManager.Store);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient(
    SupportDashboardIdentityHttpClientOptions.ClientName,
    client =>
    {
      client.Timeout = TimeSpan.FromSeconds(30);
      client.MaxResponseContentBufferSize = 1_048_576;
      client.DefaultRequestHeaders.UserAgent.ParseAdd("PitCrew-Support-Agent/1");
    })
    .ConfigurePrimaryHttpMessageHandler(CreateHttpHandler);
builder.Services.AddHttpClient(
    SupportRelayTransportHttpClientOptions.ClientName,
    client =>
    {
      client.Timeout = TimeSpan.FromSeconds(30);
      client.MaxResponseContentBufferSize = 1_048_576;
      client.DefaultRequestHeaders.UserAgent.ParseAdd("PitCrew-Support-Agent/1");
    })
    .ConfigurePrimaryHttpMessageHandler(CreateHttpHandler);
builder.Services.AddSingleton<SupportDashboardIdentityClient>();
builder.Services.AddSingleton<SupportNodeIdentityProvisioner>();
builder.Services.AddSingleton<SupportRelayTransportClient>();
builder.Services.AddSingleton<SupportAgentStartupStatusWriter>();
builder.Services.AddWindowsService(service =>
{
  service.ServiceName = "PitCrewSupportAgent";
});
if (!(rotateMode || deleteIdentityMode))
{
  builder.Services.AddHostedService<SupportAgentWorker>();
}
using var host = builder.Build();
if (deleteIdentityMode)
{
  await host.StartAsync();
  try
  {
    var removed = await host.Services
        .GetRequiredService<SupportNodeIdentityManager>()
        .RemoveAsync(
            SupportIdentityKeyRemovalChoice.DeleteKeys,
            CancellationToken.None);
    host.Services
        .GetRequiredService<SupportAgentStartupStatusWriter>()
        .Write(
            "identity-removal",
            removed
                ? "delete-keys-succeeded"
                : "delete-keys-unavailable",
            exceptionType: null);
    Environment.ExitCode = removed ? 0 : 1;
  }
  catch (Exception exception)
  {
    host.Services
        .GetRequiredService<SupportAgentStartupStatusWriter>()
        .Write(
            "identity-removal",
            "delete-keys-failed",
            exception.GetType());
    throw;
  }
  finally
  {
    await host.StopAsync();
  }
}
else if (rotateMode)
{
  var outcome = await host.Services
      .GetRequiredService<SupportNodeIdentityProvisioner>()
      .RotateAsync(CancellationToken.None);
#pragma warning disable NLF0001 // The packaged command emits one machine-readable result to stdout.
  Console.WriteLine(JsonSerializer.Serialize(new
  {
    status = outcome.Status.ToString(),
    rotationId = outcome.RotationId,
  }));
#pragma warning restore NLF0001
  Environment.ExitCode = outcome.Succeeded
      ? 0
      : outcome.Status == SupportNodeRotationStatus.FinalizationPending
          ? 2
          : 1;
}
else
{
  await host.RunAsync();
}

static HttpMessageHandler CreateHttpHandler() =>
    new HttpClientHandler
    {
      AllowAutoRedirect = false,
    };
