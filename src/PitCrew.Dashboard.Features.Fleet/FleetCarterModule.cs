using Carter;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet;

/// <summary>
/// Maps connector enrollment, synchronization, and read-only fleet endpoints.
/// </summary>
public sealed class FleetCarterModule : ICarterModule
{
  private const string EnrollmentCodeHeader = "X-PitCrew-Enrollment-Code";

  /// <summary>
  /// Adds the fleet API routes to the application.
  /// </summary>
  /// <param name="app">Endpoint route builder.</param>
  public void AddRoutes(IEndpointRouteBuilder app)
  {
    var connectors = app.MapGroup("/api/connectors/v1")
        .AllowAnonymous()
        .DisableAntiforgery();
    connectors.MapPost("/enroll", EnrollAsync);
    connectors.MapPost("/sync", SyncAsync);

    var fleet = app.MapGroup(
            "/api/tenants/{tenantId}/fleet/v1")
        .RequireAuthorization(AccessPolicies.TenantViewer);
    fleet.MapGet("/nodes", GetFleetAsync);
    fleet.MapPost("/enrollment-codes", CreateEnrollmentCodeAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .RequireAuthorization(
            AccessPolicies.TenantAdministrator);
    fleet.MapPost("/nodes/{nodeId:guid}/revoke", RevokeNodeAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .RequireAuthorization(
            AccessPolicies.TenantAdministrator);
    fleet.MapPost(
            "/nodes/{nodeId:guid}/credential-rotation",
            RequestCredentialRotationAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .RequireAuthorization(
            AccessPolicies.TenantAdministrator);
  }

  private static async Task<IResult> EnrollAsync(
      HttpContext context,
      ConnectorEnrollmentRequest request,
      IEnrollConnectorUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var enrollmentCode = context.Request.Headers[
        EnrollmentCodeHeader].ToString();
    if (string.IsNullOrWhiteSpace(enrollmentCode))
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
        enrollmentCode,
        new ConnectorEnrollmentInput(
            request.ConnectorInstanceId,
            request.DisplayName),
        cancellationToken);
    return response is null
        ? Results.Unauthorized()
        : Results.Ok(response);
  }

  private static async Task<IResult> SyncAsync(
      HttpContext context,
      ConnectorSyncRequest request,
      ISyncConnectorUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var authorization = context.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
      return Results.Unauthorized();
    }

    var result = await unitOfWork.SynchronizeAsync(
        authorization["Bearer ".Length..].Trim(),
        new ConnectorSynchronizationInput(
            request.ProtocolVersion,
            request.ConnectorVersion,
            request.SentAt,
            request.Profiles),
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
      string tenantId,
      IGetFleetUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      Results.Ok(await unitOfWork.GetAsync(
          tenantId,
          cancellationToken));

  private static async Task<IResult> CreateEnrollmentCodeAsync(
      HttpContext context,
      string tenantId,
      CreateEnrollmentCodeRequest request,
      ICreateEnrollmentCodeUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(request.Label) ||
        request.Label.Length > 128)
    {
      return Results.BadRequest(new
      {
        error = new
        {
          code = "invalid_enrollment_label",
          message =
              "Enrollment code label must be between 1 and 128 characters.",
        },
      });
    }
    var created = await unitOfWork.CreateOrNullAsync(
        context.User,
        tenantId,
        request.Label.Trim(),
        cancellationToken);
    return created is null
        ? Results.Unauthorized()
        : Results.Ok(new CreateEnrollmentCodeResponse(
            created.EnrollmentCodeId,
            created.Code,
            created.ExpiresAt));
  }

  private static async Task<IResult> RevokeNodeAsync(
      string tenantId,
      Guid nodeId,
      INodeAdministrationUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      NodeMutationResult(await unitOfWork.RevokeAsync(
          tenantId,
          nodeId,
          cancellationToken));

  private static async Task<IResult> RequestCredentialRotationAsync(
      string tenantId,
      Guid nodeId,
      INodeAdministrationUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      NodeMutationResult(
          await unitOfWork.RequestCredentialRotationAsync(
              tenantId,
              nodeId,
              cancellationToken));

  private static IResult NodeMutationResult(
      NodeMutationStatus status) =>
      status switch
      {
        NodeMutationStatus.Succeeded => Results.NoContent(),
        NodeMutationStatus.NotFound => Results.NotFound(),
        NodeMutationStatus.Revoked => Results.Conflict(new
        {
          error = new
          {
            code = "node_revoked",
            message =
                "A revoked node must re-enroll before rotating its credential.",
          },
        }),
        _ => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Unsupported node mutation result."),
      };
}
