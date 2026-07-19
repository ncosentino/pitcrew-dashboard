using Carter;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

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
    if (!IsTenantIdValid(request.TenantId) ||
        string.IsNullOrWhiteSpace(request.DisplayName) ||
        request.DisplayName.Length > 128)
    {
      return Invalid(
          "invalid_tenant",
          "Tenant ID and display name do not satisfy the tenant contract.");
    }

    var status = await unitOfWork.CreateAsync(
        context.User,
        request.TenantId,
        request.DisplayName.Trim(),
        cancellationToken);
    return status switch
    {
      AccessMutationStatus.Succeeded => Results.Created(
          $"/api/tenants/{request.TenantId}",
          new TenantAccessResponse(
              request.TenantId,
              request.DisplayName.Trim(),
              FormatRole(TenantRole.Owner))),
      AccessMutationStatus.Conflict => Conflict(
          "tenant_exists",
          "A tenant with that identifier already exists."),
      _ => Results.Forbid(),
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
