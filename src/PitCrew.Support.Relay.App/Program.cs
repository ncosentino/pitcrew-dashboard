using System.Net;
using System.Security.Cryptography;
using System.Text;

using PitCrew.Support.Relay.App;

var builder = WebApplication.CreateBuilder(args);
var relayOptions = RelayOptions.FromConfiguration(builder.Configuration);
var store = new SqliteRelayStore(relayOptions.DatabasePath);
const int MaxNodeActivityBatchSize = 256;
await store.InitializeAsync(CancellationToken.None);
builder.Services.AddSingleton(relayOptions);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(TimeProvider.System);
var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new
{
  status = "healthy",
}));

static bool HasInternalBearer(HttpRequest request, RelayOptions options)
{
  var value = request.Headers.Authorization.ToString();
  const string prefix = "Bearer ";
  return !string.IsNullOrWhiteSpace(options.InternalBearerSecret) &&
      value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
      SecretEquals(value[prefix.Length..].Trim(), options.InternalBearerSecret);
}

static bool SecretEquals(string actual, string expected)
{
  if (actual.Length is < 16 or > 4096 || expected.Length is < 16 or > 4096)
  {
    return false;
  }
  return CryptographicOperations.FixedTimeEquals(
      SHA256.HashData(Encoding.UTF8.GetBytes(actual)),
      SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}

static string? ReadBearer(HttpRequest request)
{
  var value = request.Headers.Authorization.ToString();
  const string prefix = "Bearer ";
  return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
      ? value[prefix.Length..].Trim()
      : null;
}

static IResult ToRotationResult(RelayCredentialRotationStatus status) =>
    status switch
    {
      RelayCredentialRotationStatus.Prepared or
      RelayCredentialRotationStatus.Promoted => Results.NoContent(),
      RelayCredentialRotationStatus.NotFound => Results.NotFound(),
      RelayCredentialRotationStatus.Forbidden or
      RelayCredentialRotationStatus.Revoked => Results.StatusCode(
          StatusCodes.Status403Forbidden),
      _ => Results.Conflict(),
    };

var internalApi = app.MapGroup("/internal/support/v1");
internalApi.MapPost("/nodes/activity", async (
    HttpContext context,
    RelayNodeActivityRequest request,
    RelayOptions options,
    SqliteRelayStore relayStore,
    CancellationToken cancellationToken) =>
{
  if (!HasInternalBearer(context.Request, options))
  {
    return Results.Unauthorized();
  }
  if (string.IsNullOrWhiteSpace(request.TenantId) ||
      request.TenantId.Length > 200 ||
      request.NodeIds is null ||
      request.NodeIds.Count == 0 ||
      request.NodeIds.Count > MaxNodeActivityBatchSize ||
      request.NodeIds.Count != request.NodeIds.Distinct().Count())
  {
    return Results.BadRequest();
  }
  return Results.Ok(await relayStore.GetNodeActivityAsync(
      request.TenantId,
      request.NodeIds,
      cancellationToken));
});
internalApi.MapPost("/nodes", async (
    HttpContext context,
    RelayNodeRegistrationRequest request,
    RelayOptions options,
    SqliteRelayStore relayStore,
    CancellationToken cancellationToken) =>
{
  if (!HasInternalBearer(context.Request, options))
  {
    return Results.Unauthorized();
  }
  return await relayStore.RegisterNodeAsync(request, cancellationToken)
      ? Results.NoContent()
      : Results.Conflict();
});
internalApi.MapPost("/nodes/{nodeId:guid}/revoke", async (
    HttpContext context,
    Guid nodeId,
    RelayOptions options,
    SqliteRelayStore relayStore,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
  if (!HasInternalBearer(context.Request, options))
  {
    return Results.Unauthorized();
  }
  return await relayStore.RevokeNodeAsync(nodeId, timeProvider.GetUtcNow(), cancellationToken)
      ? Results.NoContent()
      : Results.NotFound();
});
internalApi.MapPost("/nodes/{nodeId:guid}/prepare-credential", async (
    HttpContext context,
    Guid nodeId,
    RelayNodeCredentialRotationRequest request,
    RelayOptions options,
    SqliteRelayStore relayStore,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
  if (!HasInternalBearer(context.Request, options))
  {
    return Results.Unauthorized();
  }
  return ToRotationResult(await relayStore.PrepareNodeCredentialAsync(
      nodeId,
      request,
      timeProvider.GetUtcNow(),
      cancellationToken));
});
internalApi.MapPost("/nodes/{nodeId:guid}/promote-credential", async (
    HttpContext context,
    Guid nodeId,
    RelayNodeCredentialRotationRequest request,
    RelayOptions options,
    SqliteRelayStore relayStore,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
  if (!HasInternalBearer(context.Request, options))
  {
    return Results.Unauthorized();
  }
  return ToRotationResult(await relayStore.PromoteNodeCredentialAsync(
      nodeId,
      request,
      timeProvider.GetUtcNow(),
      cancellationToken));
});
internalApi.MapPost("/sessions", async (
    HttpContext context,
    RelaySessionEnqueueRequest request,
    RelayOptions options,
    SqliteRelayStore relayStore,
    CancellationToken cancellationToken) =>
{
  if (!HasInternalBearer(context.Request, options))
  {
    return Results.Unauthorized();
  }
  if (string.IsNullOrWhiteSpace(request.TenantId) ||
      string.IsNullOrWhiteSpace(request.RequestEnvelope) ||
      request.RequestEnvelope.Length > 1_048_576)
  {
    return Results.BadRequest();
  }
  return await relayStore.EnqueueSessionAsync(request, cancellationToken)
      ? Results.Accepted($"/internal/support/v1/sessions/{request.SessionId:D}")
      : Results.Conflict();
});
internalApi.MapPost("/sessions/{sessionId:guid}/cancel", async (
    HttpContext context,
    Guid sessionId,
    RelayOptions options,
    SqliteRelayStore relayStore,
    CancellationToken cancellationToken) =>
{
  if (!HasInternalBearer(context.Request, options))
  {
    return Results.Unauthorized();
  }
  return await relayStore.CancelSessionAsync(sessionId, cancellationToken)
      ? Results.NoContent()
      : Results.Conflict();
});
internalApi.MapGet("/sessions/{sessionId:guid}", async (
    HttpContext context,
    Guid sessionId,
    RelayOptions options,
    SqliteRelayStore relayStore,
    CancellationToken cancellationToken) =>
{
  if (!HasInternalBearer(context.Request, options))
  {
    return Results.Unauthorized();
  }
  var session = await relayStore.GetSessionAsync(sessionId, cancellationToken);
  return session is null ? Results.NotFound() : Results.Ok(session);
});
internalApi.MapGet("/sessions/{sessionId:guid}/result", async (
    HttpContext context,
    Guid sessionId,
    RelayOptions options,
    SqliteRelayStore relayStore,
    CancellationToken cancellationToken) =>
{
  if (!HasInternalBearer(context.Request, options))
  {
    return Results.Unauthorized();
  }
  var session = await relayStore.GetSessionAsync(sessionId, cancellationToken);
  return session?.ResultEnvelope is null
      ? Results.NotFound()
      : Results.Text(session.ResultEnvelope, "application/json");
});

var nodeApi = app.MapGroup("/api/support-relay/v1/nodes/{nodeId:guid}");
nodeApi.MapGet("/poll", async (
    HttpContext context,
    Guid nodeId,
    SqliteRelayStore relayStore,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
  var bearer = ReadBearer(context.Request);
  if (string.IsNullOrWhiteSpace(bearer))
  {
    return Results.Unauthorized();
  }
  var outcome = await relayStore.PollAsync(
      nodeId,
      bearer,
      timeProvider.GetUtcNow(),
      cancellationToken);
  if (!outcome.CredentialAccepted)
  {
    return Results.Unauthorized();
  }
  return outcome.Session is null
      ? Results.StatusCode((int)HttpStatusCode.NoContent)
      : Results.Ok(new RelayPollResponse(
          outcome.Session.SessionId,
          outcome.Session.RequestEnvelope,
          outcome.Session.ExpiresAt));
});
nodeApi.MapPost("/sessions/{sessionId:guid}/result", async (
    HttpContext context,
    Guid nodeId,
    Guid sessionId,
    RelayResultUploadRequest request,
    SqliteRelayStore relayStore,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
  var bearer = ReadBearer(context.Request);
  if (string.IsNullOrWhiteSpace(bearer))
  {
    return Results.Unauthorized();
  }
  if (string.IsNullOrWhiteSpace(request.ResultEnvelope) ||
      request.ResultEnvelope.Length > 4_194_304)
  {
    return Results.BadRequest();
  }
  var outcome = await relayStore.UploadResultAsync(
      nodeId,
      sessionId,
      bearer,
      request.ResultEnvelope,
      timeProvider.GetUtcNow(),
      cancellationToken);
  return outcome switch
  {
    RelayResultUploadOutcome.Succeeded => Results.NoContent(),
    RelayResultUploadOutcome.CredentialRejected => Results.Unauthorized(),
    _ => Results.NotFound(),
  };
});

await app.RunAsync();

/// <summary>
/// Test hook for WebApplicationFactory.
/// </summary>
public partial class Program;
