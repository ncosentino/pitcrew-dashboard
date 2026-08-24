using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PitCrew.Support.Agent.App;

var rotateMode = args.Length == 1 &&
    string.Equals(args[0], "rotate", StringComparison.OrdinalIgnoreCase);
var finalizeEnrollmentMode = args.Length == 1 &&
    string.Equals(
        args[0],
        "finalize-enrollment",
        StringComparison.OrdinalIgnoreCase);
var builder = Host.CreateApplicationBuilder(args);
var identityDeletionRequestPresent =
    SupportIdentityDeletionRequestWorker.IsRequestPresent(
        builder.Environment.ContentRootPath);
var enrollmentFinalizationRequestPresent =
    SupportEnrollmentFinalizationRequestWorker.IsRequestPresent(
        builder.Environment.ContentRootPath);
if (identityDeletionRequestPresent &&
    enrollmentFinalizationRequestPresent)
{
  throw new InvalidOperationException(
      "Conflicting local support lifecycle requests are present.");
}
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
if (!rotateMode && !finalizeEnrollmentMode)
{
  if (!identityDeletionRequestPresent &&
      !enrollmentFinalizationRequestPresent)
  {
    builder.Services.AddHostedService<SupportAgentWorker>();
  }
  builder.Services.AddHostedService<SupportEnrollmentFinalizationRequestWorker>();
  builder.Services.AddHostedService<SupportIdentityDeletionRequestWorker>();
}
using var host = builder.Build();
if (rotateMode)
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
else if (finalizeEnrollmentMode)
{
  await using var operationLock =
      await identityManager.Store.AcquireOperationLockAsync(
          CancellationToken.None);
  var activeIdentity = await identityManager.Store.LoadActiveAsync(
      CancellationToken.None);
  var status = activeIdentity is null
      ? SupportEnrollmentFinalizationStatus.ActiveIdentityUnavailable
      : SupportAgentSettingsFinalizer.Finalize(
          builder.Environment.ContentRootPath);
#pragma warning disable NLF0001 // The packaged command emits one machine-readable result to stdout.
  Console.WriteLine(JsonSerializer.Serialize(new
  {
    status = status.ToString(),
  }));
#pragma warning restore NLF0001
  Environment.ExitCode = status is
      SupportEnrollmentFinalizationStatus.Succeeded or
      SupportEnrollmentFinalizationStatus.AlreadyFinalized
          ? 0
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
