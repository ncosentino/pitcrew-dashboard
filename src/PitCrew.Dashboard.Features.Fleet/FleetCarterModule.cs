using Carter;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;
using PitCrew.Dashboard.Kernel.DisplayNames;
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
    fleet.MapGet("/history/capabilities", GetHistoryCapabilities);
    fleet.MapGet("/incidents", GetIncidentsAsync);
    fleet.MapGet("/nodes/{nodeId:guid}/history", GetNodeHistoryAsync);
    fleet.MapGet(
        "/nodes/{nodeId:guid}/profiles/{profileId}/history",
        GetProfileHistoryAsync);
    fleet.MapPost("/enrollment-codes", CreateEnrollmentCodeAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .RequireAuthorization(
            AccessPolicies.TenantAdministrator);
    fleet.MapPut("/nodes/{nodeId:guid}", RenameNodeAsync)
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
    fleet.MapPost(
            "/nodes/{nodeId:guid}/profiles/{profileId}/capacity-maximum",
            SetCapacityMaximumAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .RequireAuthorization(
            AccessPolicies.TenantAdministrator);
    fleet.MapPost(
            "/nodes/{nodeId:guid}/profiles/{profileId}/manager-recovery",
            RecoverManagerAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .RequireAuthorization(
            AccessPolicies.TenantAdministrator);
    fleet.MapPost(
            "/incidents/{incidentId:guid}/acknowledge",
            AcknowledgeIncidentAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .RequireAuthorization(
            AccessPolicies.TenantAdministrator);
    fleet.MapPost(
            "/incidents/{incidentId:guid}/unacknowledge",
            UnacknowledgeIncidentAsync)
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
    var displayName = OperatorDisplayName.NormalizeOrNull(
        request.DisplayName);
    if (string.IsNullOrWhiteSpace(request.ConnectorInstanceId) ||
        request.ConnectorInstanceId.Length > 128 ||
        displayName is null)
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
            displayName),
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
            request.Profiles,
            request.CapacityOperator,
            request.CapacityCommandOutcome,
            request.RecoveryOperator,
            request.RecoveryCommandProgress,
            request.RecoveryCommandOutcome,
            request.ConnectorHealth,
            request.ImageRolloutOperator,
            request.ImageRolloutCommandProgress,
            request.ImageRolloutCommandOutcome),
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

  private static IResult GetHistoryCapabilities(
      IGetFleetHistoryUnitOfWork unitOfWork) =>
      Results.Ok(unitOfWork.GetCapabilities());

  private static async Task<IResult> GetIncidentsAsync(
      HttpContext context,
      string tenantId,
      IGetAlertsUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var query = context.Request.Query;
    var result = await unitOfWork.GetAsync(
        tenantId,
        new AlertQueryInput(
            query["status"].ToString(),
            query["limit"].ToString()),
        cancellationToken);
    return result.Status switch
    {
      AlertQueryStatus.Succeeded => Results.Ok(
          ToResponse(result.Page ??
              throw new InvalidOperationException(
                  "A successful incident query did not return a page."))),
      AlertQueryStatus.Invalid => Results.BadRequest(new
      {
        error = new
        {
          code = "invalid_incident_query",
          message = result.Error,
        },
      }),
      _ => Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Unsupported incident query result."),
    };
  }

  private static async Task<IResult> GetNodeHistoryAsync(
      HttpContext context,
      string tenantId,
      Guid nodeId,
      IGetFleetHistoryUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      HistoryResult(await unitOfWork.GetNodeHistoryAsync(
          tenantId,
          nodeId,
          ReadHistoryQuery(context),
          cancellationToken));

  private static async Task<IResult> GetProfileHistoryAsync(
      HttpContext context,
      string tenantId,
      Guid nodeId,
      string profileId,
      IGetFleetHistoryUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      HistoryResult(await unitOfWork.GetProfileHistoryAsync(
          tenantId,
          nodeId,
          profileId,
          ReadHistoryQuery(context),
          cancellationToken));

  private static HistoryQueryInput ReadHistoryQuery(HttpContext context)
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

  private static IResult HistoryResult(HistoryQueryResult result) =>
      result.Status switch
      {
        HistoryQueryStatus.Succeeded => Results.Ok(result.Response),
        HistoryQueryStatus.NotFound => Results.NotFound(),
        HistoryQueryStatus.Invalid => Results.BadRequest(new
        {
          error = new
          {
            code = "invalid_history_query",
            message = result.Error,
          },
        }),
        _ => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Unsupported history query result."),
      };

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

  private static async Task<IResult> RenameNodeAsync(
      string tenantId,
      Guid nodeId,
      RenameNodeRequest request,
      INodeAdministrationUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var displayName = OperatorDisplayName.NormalizeOrNull(
        request.DisplayName);
    if (displayName is null)
    {
      return Results.BadRequest(new
      {
        error = new
        {
          code = "invalid_node_name",
          message =
              "Server display name must contain between 1 and 128 characters.",
        },
      });
    }

    var status = await unitOfWork.RenameAsync(
        tenantId,
        nodeId,
        displayName,
        cancellationToken);
    return status switch
    {
      NodeMutationStatus.Succeeded => Results.NoContent(),
      NodeMutationStatus.NotFound => Results.NotFound(),
      _ => Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Unsupported node rename result."),
    };
  }

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

  private static async Task<IResult> AcknowledgeIncidentAsync(
      HttpContext context,
      string tenantId,
      Guid incidentId,
      IAcknowledgeAlertUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var status = await unitOfWork.AcknowledgeOrNullAsync(
        context.User,
        tenantId,
        incidentId,
        cancellationToken);
    return status switch
    {
      null => Results.Unauthorized(),
      AlertAcknowledgeStatus.Succeeded => Results.NoContent(),
      AlertAcknowledgeStatus.NotFound => Results.NotFound(),
      AlertAcknowledgeStatus.Resolved => Results.Conflict(new
      {
        error = new
        {
          code = "incident_resolved",
          message = "The incident resolved before it could be acknowledged.",
        },
      }),
      _ => Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Unsupported incident acknowledgement result."),
    };
  }

  private static async Task<IResult> UnacknowledgeIncidentAsync(
      HttpContext context,
      string tenantId,
      Guid incidentId,
      IUnacknowledgeAlertUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var status = await unitOfWork.UnacknowledgeOrNullAsync(
        context.User,
        tenantId,
        incidentId,
        cancellationToken);
    return status switch
    {
      null => Results.Unauthorized(),
      AlertUnacknowledgeStatus.Succeeded => Results.NoContent(),
      AlertUnacknowledgeStatus.AlreadyTriggered => Results.NoContent(),
      AlertUnacknowledgeStatus.NotFound => Results.NotFound(),
      AlertUnacknowledgeStatus.Resolved => Results.Conflict(new
      {
        error = new
        {
          code = "incident_resolved",
          message = "The incident resolved before it could be unacknowledged.",
        },
      }),
      _ => Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Unsupported incident unacknowledgement result."),
    };
  }

  private static async Task<IResult> SetCapacityMaximumAsync(
      HttpContext context,
      string tenantId,
      Guid nodeId,
      string profileId,
      SetCapacityMaximumRequest request,
      ISetCapacityMaximumUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    if (!SyncConnectorUnitOfWork.IsValidProfileId(profileId) ||
        request.Maximum is < 0 or > 1_000_000)
    {
      return Results.BadRequest(new
      {
        error = new
        {
          code = "invalid_capacity_request",
          message =
              "Profile ID must be valid and maximum must be between 0 and 1000000.",
        },
      });
    }

    var result = await unitOfWork.QueueOrNullAsync(
        context.User,
        tenantId,
        nodeId,
        profileId,
        request.Maximum,
        request.ResumeCommandId,
        cancellationToken);
    if (result is null)
    {
      return Results.Unauthorized();
    }
    return result.Status switch
    {
      CapacityCommandQueueStatus.Queued => Results.Accepted(
          value: new SetCapacityMaximumResponse(
              result.CommandId!.Value,
              "pending")),
      CapacityCommandQueueStatus.NodeNotFound => Results.NotFound(),
      CapacityCommandQueueStatus.Unsupported => Results.Conflict(new
      {
        error = new
        {
          code = "capacity_not_supported",
          message =
              "The connector has not enabled capacity operations for this profile.",
        },
      }),
      CapacityCommandQueueStatus.InvalidMaximum => Results.BadRequest(new
      {
        error = new
        {
          code = "capacity_out_of_policy",
          message =
              "The requested maximum is unchanged or outside the connector's local capacity policy.",
        },
      }),
      CapacityCommandQueueStatus.Conflict => Results.Conflict(new
      {
        error = new
        {
          code = "capacity_command_active",
          message =
              "Another capacity command is already active for this profile.",
        },
      }),
      CapacityCommandQueueStatus.StaleResume => Results.Conflict(new
      {
        error = new
        {
          code = "capacity_resume_stale",
          message =
              "The recorded pause no longer matches the current profile generation. Set an explicit positive maximum instead.",
        },
      }),
      _ => Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Unsupported capacity command result."),
    };
  }

  private static async Task<IResult> RecoverManagerAsync(
      HttpContext context,
      string tenantId,
      Guid nodeId,
      string profileId,
      RecoverManagerRequest request,
      IRecoverManagerUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    if (!SyncConnectorUnitOfWork.IsValidProfileId(profileId) ||
        !SyncConnectorUnitOfWork.IsValidRecoveryFences(
            request.ExpectedManagerInstanceId,
            request.ExpectedGeneration,
            request.ExpectedDesiredStateHash))
    {
      return Results.BadRequest(new
      {
        error = new
        {
          code = "invalid_recovery_request",
          message =
              "Profile ID, expected manager instance, generation, and desired-state hash must be valid.",
        },
      });
    }

    var result = await unitOfWork.QueueOrNullAsync(
        context.User,
        tenantId,
        nodeId,
        profileId,
        new RecoveryCommandFences(
            request.ExpectedManagerInstanceId,
            request.ExpectedGeneration,
            request.ExpectedDesiredStateHash),
        cancellationToken);
    if (result is null)
    {
      return Results.Unauthorized();
    }
    return result.Status switch
    {
      RecoveryCommandQueueStatus.Queued => Results.Accepted(
          value: new RecoverManagerResponse(
              result.CommandId!.Value,
              "queued")),
      RecoveryCommandQueueStatus.NodeNotFound => Results.NotFound(),
      RecoveryCommandQueueStatus.Unsupported => Results.Conflict(new
      {
        error = new
        {
          code = "recovery_not_supported",
          message =
              "The connector has not enabled manager recovery for this profile.",
        },
      }),
      RecoveryCommandQueueStatus.NotAllowed => Results.Conflict(new
      {
        error = new
        {
          code = "recovery_not_allowed",
          message =
              "Local connector policy currently disallows manager recovery for this profile.",
        },
      }),
      RecoveryCommandQueueStatus.StaleFence => Results.Conflict(new
      {
        error = new
        {
          code = "recovery_fence_stale",
          message =
              "The expected manager instance, generation, desired-state hash, or projection is no longer current.",
        },
      }),
      RecoveryCommandQueueStatus.Conflict => Results.Conflict(new
      {
        error = new
        {
          code = "profile_operation_active",
          message =
              "Another operation is already active for this profile.",
        },
      }),
      RecoveryCommandQueueStatus.RateLimited => Results.StatusCode(
          StatusCodes.Status429TooManyRequests),
      _ => Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Unsupported manager recovery result."),
    };
  }

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

  private static AlertIncidentListResponse ToResponse(
      AlertIncidentPage page) =>
      new(
            page.GeneratedAt,
            page.Incidents.Select(incident => new AlertIncidentResponse(
                incident.IncidentId,
                incident.NodeId,
                incident.ProfileId,
                incident.Kind,
                incident.Severity,
                incident.Status,
                incident.Title,
                incident.Summary,
                incident.Reason,
                incident.Evidence,
                incident.Link,
                incident.FirstObservedAt,
                incident.TriggeredAt,
                incident.LastObservedAt,
                incident.AcknowledgedAt,
                incident.AcknowledgedByGitHubUserId,
                incident.ResolvedAt))
                .ToArray(),
            page.Truncated);
}
