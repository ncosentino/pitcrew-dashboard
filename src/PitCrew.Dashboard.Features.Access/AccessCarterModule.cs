using Carter;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;
using PitCrew.Dashboard.Kernel.DisplayNames;

namespace PitCrew.Dashboard.Features.Access;

/// <summary>
/// Maps authenticated session, tenant, and membership endpoints.
/// </summary>
public sealed class AccessCarterModule : ICarterModule
{
  /// <summary>
  /// Adds access-management routes to the application.
  /// </summary>
  /// <param name="app">Endpoint route builder.</param>
  public void AddRoutes(IEndpointRouteBuilder app)
  {
    app.MapGet("/api/session", GetSessionAsync)
        .RequireAuthorization();
    app.MapPost("/api/tenants", CreateTenantAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .RequireAuthorization(AccessPolicies.SystemAdministrator);
    app.MapPut("/api/tenants/{tenantId}", RenameTenantAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>()
        .RequireAuthorization(AccessPolicies.TenantOwner);

    var owners = app.MapGroup("/api/tenants/{tenantId}")
        .RequireAuthorization(AccessPolicies.TenantOwner);
    owners.MapGet("/members", GetMembersAsync);
    owners.MapGet("/available-users", GetAvailableUsersAsync);
    owners.MapPut(
            "/members/{githubUserId}",
            SetMembershipAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>();
    owners.MapDelete(
            "/members/{githubUserId}",
            RemoveMembershipAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>();

    var administrators = app.MapGroup("/api/tenants/{tenantId}")
        .RequireAuthorization(AccessPolicies.TenantAdministrator);
    administrators.MapGet(
        "/diagnostic-credentials",
        GetDiagnosticCredentialsAsync);
    administrators.MapPost(
            "/diagnostic-credentials",
            CreateDiagnosticCredentialAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>();
    administrators.MapPost(
            "/diagnostic-credentials/{credentialId:guid}/revoke",
            RevokeDiagnosticCredentialAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>();
    administrators.MapPost(
            "/diagnostic-credentials/{credentialId:guid}/rotate",
            RotateDiagnosticCredentialAsync)
        .AddEndpointFilter<DashboardAntiforgeryEndpointFilter>();
  }

  private static async Task<IResult> GetSessionAsync(
      HttpContext context,
      IAntiforgery antiforgery,
      IGetDashboardSessionUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var session = await unitOfWork.GetOrNullAsync(
        context.User,
        cancellationToken);
    if (session is null)
    {
      return Results.Unauthorized();
    }
    var tokens = antiforgery.GetAndStoreTokens(context);
    if (string.IsNullOrWhiteSpace(tokens.RequestToken))
    {
      return Results.Problem(
          statusCode: StatusCodes.Status500InternalServerError,
          title: "Antiforgery token generation failed.");
    }

    return Results.Ok(new DashboardSessionResponse(
        MapUser(session.User),
        session.IsSystemAdministrator,
        session.Tenants.Select(tenant => new TenantAccessResponse(
            tenant.TenantId,
            tenant.DisplayName,
            FormatRole(tenant.Role))).ToArray(),
        tokens.RequestToken));
  }

  private static async Task<IResult> CreateTenantAsync(
      HttpContext context,
      CreateTenantRequest request,
      ICreateTenantUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var displayName = OperatorDisplayName.NormalizeOrNull(
        request.DisplayName);
    if (!IsTenantIdValid(request.TenantId) ||
        displayName is null)
    {
      return Invalid(
          "invalid_tenant",
          "Tenant ID and display name do not satisfy the tenant contract.");
    }

    var status = await unitOfWork.CreateAsync(
        context.User,
        request.TenantId,
        displayName,
        cancellationToken);
    return status switch
    {
      AccessMutationStatus.Succeeded => Results.Created(
          $"/api/tenants/{request.TenantId}",
          new TenantAccessResponse(
              request.TenantId,
              displayName,
              FormatRole(TenantRole.Owner))),
      AccessMutationStatus.Conflict => Conflict(
          "tenant_exists",
          "A tenant with that identifier already exists."),
      _ => Results.Forbid(),
    };
  }

  private static async Task<IResult> RenameTenantAsync(
      string tenantId,
      RenameTenantRequest request,
      IRenameTenantUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var displayName = OperatorDisplayName.NormalizeOrNull(
        request.DisplayName);
    if (displayName is null)
    {
      return Invalid(
          "invalid_tenant_name",
          "Tenant display name must contain between 1 and 128 characters.");
    }

    var status = await unitOfWork.RenameAsync(
        tenantId,
        displayName,
        cancellationToken);
    return status switch
    {
      AccessMutationStatus.Succeeded => Results.NoContent(),
      AccessMutationStatus.NotFound => Results.NotFound(),
      _ => Conflict(
          "tenant_rename_conflict",
          "The tenant display name could not be changed."),
    };
  }

  private static async Task<IResult> GetMembersAsync(
      string tenantId,
      IGetTenantMembershipsUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var members = await unitOfWork.GetMembersAsync(
        tenantId,
        cancellationToken);
    return Results.Ok(members.Select(member => new TenantMemberResponse(
        MapUser(member.User),
        FormatRole(member.Role),
        member.CreatedAt)));
  }

  private static async Task<IResult> GetAvailableUsersAsync(
      string tenantId,
      IGetTenantMembershipsUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var users = await unitOfWork.GetAvailableUsersAsync(
        tenantId,
        cancellationToken);
    return Results.Ok(users.Select(MapUser));
  }

  private static async Task<IResult> SetMembershipAsync(
      HttpContext context,
      string tenantId,
      string githubUserId,
      SetTenantMembershipRequest request,
      ISetTenantMembershipUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    var role = ParseRoleOrNull(request.Role);
    if (role is null)
    {
      return Invalid(
          "invalid_role",
          "Role must be viewer, administrator, or owner.");
    }
    var status = await unitOfWork.SetAsync(
        context.User,
        tenantId,
        githubUserId,
        role.Value,
        cancellationToken);
    return MutationResult(status);
  }

  private static async Task<IResult> RemoveMembershipAsync(
      string tenantId,
      string githubUserId,
      IRemoveTenantMembershipUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      MutationResult(await unitOfWork.RemoveAsync(
          tenantId,
          githubUserId,
          cancellationToken));

  private static async Task<IResult> GetDiagnosticCredentialsAsync(
      HttpContext context,
      string tenantId,
      IDiagnosticCredentialUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok((await unitOfWork.GetAllAsync(
        tenantId,
        cancellationToken)).Select(MapDiagnosticCredential));
  }

  private static async Task<IResult> CreateDiagnosticCredentialAsync(
      HttpContext context,
      string tenantId,
      CreateDiagnosticCredentialRequest request,
      IDiagnosticCredentialUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    var result = await unitOfWork.CreateAsync(
        context.User,
        tenantId,
        new CreateDiagnosticCredentialInput(
            request.Label,
            request.ExpiresAt,
            request.NodeIds ?? [],
            request.ProfileIds ?? []),
        cancellationToken);
    return DiagnosticCredentialResult(
        result,
        created: true);
  }

  private static async Task<IResult> RevokeDiagnosticCredentialAsync(
      HttpContext context,
      string tenantId,
      Guid credentialId,
      IDiagnosticCredentialUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      DiagnosticCredentialMutationResult(
          await unitOfWork.RevokeAsync(
              context.User,
              tenantId,
              credentialId,
              cancellationToken));

  private static Task<IResult> RotateDiagnosticCredentialAsync(
      HttpContext context,
      string tenantId,
      Guid credentialId,
      IDiagnosticCredentialUnitOfWork unitOfWork,
      CancellationToken cancellationToken) =>
      RotateDiagnosticCredentialResultAsync(
          context,
          tenantId,
          credentialId,
          unitOfWork,
          cancellationToken);

  private static async Task<IResult> RotateDiagnosticCredentialResultAsync(
      HttpContext context,
      string tenantId,
      Guid credentialId,
      IDiagnosticCredentialUnitOfWork unitOfWork,
      CancellationToken cancellationToken)
  {
    context.Response.Headers.CacheControl = "no-store";
    return DiagnosticCredentialResult(
        await unitOfWork.RotateAsync(
            context.User,
            tenantId,
            credentialId,
            cancellationToken),
        created: false);
  }

  private static IResult DiagnosticCredentialResult(
      DiagnosticCredentialCommandResult result,
      bool created)
  {
    if (result.Status ==
            DiagnosticCredentialMutationStatus.Succeeded &&
        result.Credential is not null &&
        result.RawCredential is not null)
    {
      var response = new DiagnosticCredentialCreatedResponse(
          MapDiagnosticCredential(result.Credential),
          result.RawCredential);
      return created
          ? Results.Created(
              $"/api/tenants/{result.Credential.TenantId}/diagnostic-credentials/{result.Credential.CredentialId:D}",
              response)
          : Results.Ok(response);
    }
    if (result.Error is not null)
    {
      return Invalid(
          "invalid_diagnostic_credential",
          result.Error);
    }
    return result.Status switch
    {
      DiagnosticCredentialMutationStatus.NotFound =>
          Results.NotFound(),
      DiagnosticCredentialMutationStatus.InvalidNode =>
          Invalid(
              "invalid_diagnostic_node",
              "Every node restriction must belong to the tenant."),
      _ => Conflict(
          "diagnostic_credential_conflict",
          "The diagnostic credential could not be changed."),
    };
  }

  private static IResult DiagnosticCredentialMutationResult(
      DiagnosticCredentialMutationStatus status) =>
      status switch
      {
        DiagnosticCredentialMutationStatus.Succeeded =>
            Results.NoContent(),
        DiagnosticCredentialMutationStatus.NotFound =>
            Results.NotFound(),
        _ => Conflict(
            "diagnostic_credential_conflict",
            "The diagnostic credential could not be changed."),
      };

  private static DiagnosticCredentialResponse MapDiagnosticCredential(
      DiagnosticCredential credential) =>
      new(
          credential.CredentialId,
          credential.Label,
          credential.CreatedByGitHubUserId,
          credential.CreatedAt,
          credential.ExpiresAt,
          credential.RevokedAt,
          credential.RevokedByGitHubUserId,
          credential.RotatedFromCredentialId,
          credential.LastUsedAt,
          credential.UseCount,
          credential.NodeIds,
          credential.ProfileIds);

  private static IResult MutationResult(
      AccessMutationStatus status) =>
      status switch
      {
        AccessMutationStatus.Succeeded => Results.NoContent(),
        AccessMutationStatus.NotFound => Results.NotFound(),
        AccessMutationStatus.LastOwner => Conflict(
            "last_owner",
            "A tenant must retain at least one owner."),
        _ => Conflict(
            "membership_conflict",
            "The tenant membership could not be changed."),
      };

  private static DashboardUserResponse MapUser(DashboardUser user) =>
      new(
          user.GitHubUserId,
          user.GitHubLogin,
          user.DisplayName,
          user.AvatarUrl);

  private static string FormatRole(TenantRole role) =>
      role switch
      {
        TenantRole.Viewer => "viewer",
        TenantRole.Administrator => "administrator",
        TenantRole.Owner => "owner",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
      };

  private static TenantRole? ParseRoleOrNull(string value) =>
      value.Trim().ToLowerInvariant() switch
    {
      "viewer" => TenantRole.Viewer,
      "administrator" => TenantRole.Administrator,
      "owner" => TenantRole.Owner,
      _ => null,
    };

  private static bool IsTenantIdValid(string tenantId)
  {
    if (tenantId.Length is < 1 or > 64 ||
        tenantId[0] is < 'a' or > 'z')
    {
      return false;
    }
    return tenantId.All(character =>
        character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-');
  }

  private static IResult Invalid(
      string code,
      string message) =>
      Results.BadRequest(new
      {
        error = new
        {
          code,
          message,
        },
      });

  private static IResult Conflict(
      string code,
      string message) =>
      Results.Conflict(new
      {
        error = new
        {
          code,
          message,
        },
      });
}
