using System.Net;

using PitCrew.Support.Relay.App;

var builder = WebApplication.CreateBuilder(args);
var relayOptions = RelayOptions.FromConfiguration(builder.Configuration);
var store = new SqliteRelayStore(relayOptions.DatabasePath);
await store.InitializeAsync(CancellationToken.None);
builder.Services.AddSingleton(relayOptions);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(TimeProvider.System);
var app = builder.Build();

static bool HasInternalBearer(HttpRequest request, RelayOptions options)
{
  var value = request.Headers.Authorization.ToString();
  const string prefix = "Bearer ";
  return !string.IsNullOrWhiteSpace(options.InternalBearerSecret) &&
      value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(value[prefix.Length..].Trim(), options.InternalBearerSecret, StringComparison.Ordinal);
}

static string? ReadBearer(HttpRequest request)
{
  var value = request.Headers.Authorization.ToString();
  const string prefix = "Bearer ";
  return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
      ? value[prefix.Length..].Trim()
      : null;
}

var internalApi = app.MapGroup("/internal/support/v1");
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
  await relayStore.RegisterNodeAsync(request, cancellationToken);
  return Results.NoContent();
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
  await relayStore.EnqueueSessionAsync(request, cancellationToken);
  return Results.Accepted($"/internal/support/v1/sessions/{request.SessionId:D}");
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
  var session = await relayStore.PollAsync(nodeId, bearer, timeProvider.GetUtcNow(), cancellationToken);
  return session is null
      ? Results.StatusCode((int)HttpStatusCode.NoContent)
      : Results.Ok(new RelayPollResponse(session.SessionId, session.RequestEnvelope, session.ExpiresAt));
});
nodeApi.MapPost("/sessions/{sessionId:guid}/result", async (
    HttpContext context,
    Guid nodeId,
    Guid sessionId,
    RelayResultUploadRequest request,
    SqliteRelayStore relayStore,
    CancellationToken cancellationToken) =>
{
  var bearer = ReadBearer(context.Request);
  if (string.IsNullOrWhiteSpace(bearer))
  {
    return Results.Unauthorized();
  }
  return await relayStore.UploadResultAsync(nodeId, sessionId, bearer, request.ResultEnvelope, cancellationToken)
      ? Results.NoContent()
      : Results.NotFound();
});

await app.RunAsync();

/// <summary>
/// Test hook for WebApplicationFactory.
/// </summary>
public partial class Program;


