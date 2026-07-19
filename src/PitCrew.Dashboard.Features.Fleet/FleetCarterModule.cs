using Carter;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Maps connector enrollment, synchronization, and read-only fleet endpoints.
/// </summary>
public sealed class FleetCarterModule : ICarterModule
{
  private const string EnrollmentTokenHeader = "X-PitCrew-Enrollment-Token";

  /// <summary>
  /// Adds the fleet API routes to the application.
  /// </summary>
  /// <param name="app">Endpoint route builder.</param>
  public void AddRoutes(IEndpointRouteBuilder app)
  {
    var connectors = app.MapGroup("/api/connectors/v1");
    connectors.MapPost("/enroll", EnrollAsync);
    connectors.MapPost("/sync", SyncAsync);
    app.MapGet("/api/fleet/v1/nodes", GetFleetAsync);
  }

  private static async Task<IResult> EnrollAsync(
      HttpContext context,
      ConnectorEnrollmentRequest request,
      EnrollConnectorUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var enrollmentToken = context.Request.Headers[EnrollmentTokenHeader].ToString();
    if (string.IsNullOrWhiteSpace(enrollmentToken))
    {
      return Results.Unauthorized();
    }
    if (string.IsNullOrWhiteSpace(request.ConnectorInstanceId) ||
        request.ConnectorInstanceId.Length > 128 ||
        string.IsNullOrWhiteSpace(request.DisplayName) ||
        request.DisplayName.Length > 128)
    {
      return Results.BadRequest(new
      {
        error = new
        {
          code = "invalid_enrollment",
          message = "Connector instance ID and display name must be between 1 and 128 characters.",
        },
      });
    }

    var response = await unitOfWork.EnrollOrNullAsync(
        enrollmentToken,
        request,
        cancellationToken);
    return response is null
        ? Results.Unauthorized()
        : Results.Ok(response);
  }

  private static async Task<IResult> SyncAsync(
      HttpContext context,
      ConnectorSyncRequest request,
      SyncConnectorUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var authorization = context.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
      return Results.Unauthorized();
    }

    var result = await unitOfWork.SynchronizeAsync(
        authorization["Bearer ".Length..].Trim(),
        request,
        cancellationToken);
    return result.Status switch
    {
      ConnectorSyncStatus.Accepted => Results.Ok(result.Response),
      ConnectorSyncStatus.Unauthorized => Results.Unauthorized(),
      ConnectorSyncStatus.Invalid => Results.BadRequest(new
      {
        error = new
        {
          code = "invalid_sync",
          message = result.Error,
        },
      }),
      _ => Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Unsupported connector synchronization result."),
    };
  }

  private static async Task<IResult> GetFleetAsync(
      GetFleetUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      Results.Ok(await unitOfWork.GetAsync(cancellationToken));
}
