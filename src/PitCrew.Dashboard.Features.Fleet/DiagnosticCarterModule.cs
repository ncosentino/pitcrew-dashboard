using Carter;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using PitCrew.Dashboard.Features.Access;

namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Maps bounded read-only fleet diagnostics for noninteractive clients.
/// </summary>
public sealed class DiagnosticCarterModule : ICarterModule
{
  /// <summary>
  /// Adds scoped diagnostic routes to the application.
  /// </summary>
  /// <param name="app">Endpoint route builder.</param>
  public void AddRoutes(IEndpointRouteBuilder app)
  {
    var diagnostics = app.MapGroup(
            "/api/diagnostics/v1/tenants/{tenantId}/fleet")
        .DisableAntiforgery()
        .RequireAuthorization(AccessPolicies.DiagnosticsReader)
        .AddDiagnosticRateLimit();
    diagnostics.MapGet("/nodes", GetFleetAsync);
    diagnostics.MapGet(
        "/history/capabilities",
        GetHistoryCapabilities);
    diagnostics.MapGet(
        "/nodes/{nodeId:guid}/history",
        GetNodeHistoryAsync);
    diagnostics.MapGet(
        "/nodes/{nodeId:guid}/profiles/{profileId}/history",
        GetProfileHistoryAsync);
  }

  private static async Task<IResult> GetFleetAsync(
      HttpContext context,
      string tenantId,
      IGetDiagnosticFleetUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var result = await unitOfWork.GetFleetAsync(
        context.User,
        tenantId,
        new DiagnosticFleetQueryInput(
            context.Request.Query["afterNodeId"].ToString(),
            context.Request.Query["limit"].ToString()),
        cancellationToken);
    return Result(result);
  }

  private static IResult GetHistoryCapabilities(
      HttpContext context,
      IGetDiagnosticFleetUnitOfWork unitOfWork)
  {
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(unitOfWork.GetCapabilities());
  }

  private static async Task<IResult> GetNodeHistoryAsync(
      HttpContext context,
      string tenantId,
      Guid nodeId,
      IGetDiagnosticFleetUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      await GetNodeHistoryResultAsync(
          context,
          tenantId,
          nodeId,
          unitOfWork,
          cancellationToken);

  private static async Task<IResult> GetProfileHistoryAsync(
      HttpContext context,
      string tenantId,
      Guid nodeId,
      string profileId,
      IGetDiagnosticFleetUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      await GetProfileHistoryResultAsync(
          context,
          tenantId,
          nodeId,
          profileId,
          unitOfWork,
          cancellationToken);

  private static async Task<IResult> GetNodeHistoryResultAsync(
      HttpContext context,
      string tenantId,
      Guid nodeId,
      IGetDiagnosticFleetUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    return Result(await unitOfWork.GetNodeHistoryAsync(
        context.User,
        tenantId,
        nodeId,
        ReadHistoryQuery(context),
        cancellationToken));
  }

  private static async Task<IResult> GetProfileHistoryResultAsync(
      HttpContext context,
      string tenantId,
      Guid nodeId,
      string profileId,
      IGetDiagnosticFleetUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    return Result(await unitOfWork.GetProfileHistoryAsync(
        context.User,
        tenantId,
        nodeId,
        profileId,
        ReadHistoryQuery(context),
        cancellationToken));
  }

  private static HistoryQueryInput ReadHistoryQuery(
      HttpContext context)
  {
    var query = context.Request.Query;
    return new HistoryQueryInput(
        query["from"].ToString(),
        query["to"].ToString(),
        query["resolution"].ToString(),
        query["points"].ToString(),
        query["events"].ToString(),
        query["diagnostics"].ToString());
  }

  private static IResult Result(
      DiagnosticFleetQueryResult result) =>
      result.Status switch
      {
        DiagnosticQueryStatus.Succeeded when result.Fleet is not null =>
            Results.Ok(result.Fleet),
        DiagnosticQueryStatus.Succeeded when result.History is not null =>
            Results.Ok(result.History),
        DiagnosticQueryStatus.Invalid => Results.BadRequest(new
        {
          error = new
          {
            code = "invalid_diagnostic_query",
            message = result.Error,
          },
        }),
        DiagnosticQueryStatus.Forbidden => Results.Forbid(),
        DiagnosticQueryStatus.NotFound => Results.NotFound(),
        _ => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Unsupported diagnostic query result."),
      };
}
