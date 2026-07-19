using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Access.Abstractions;

namespace PitCrew.Dashboard.Features.Access;

internal sealed record TenantAccessRequirement(TenantRole MinimumRole) :
    IAuthorizationRequirement;

internal sealed record SystemAdministratorRequirement :
    IAuthorizationRequirement;

[DoNotAutoRegister]
internal sealed class TenantAuthorizationHandler(
    AccessContextService _accessContextService,
    IAccessStore _accessStore) :
    AuthorizationHandler<TenantAccessRequirement>
{
  protected override async Task HandleRequirementAsync(
      AuthorizationHandlerContext context,
      TenantAccessRequirement requirement)
  {
    if (context.Resource is not HttpContext httpContext)
    {
      return;
    }
    var tenantId = Convert.ToString(
        httpContext.Request.RouteValues["tenantId"],
        System.Globalization.CultureInfo.InvariantCulture);
    if (string.IsNullOrWhiteSpace(tenantId))
    {
      return;
    }

    var accessContext = await _accessContextService.GetOrNullAsync(
        context.User,
        httpContext.RequestAborted);
    if (accessContext is null)
    {
      return;
    }
    if (accessContext.IsSystemAdministrator)
    {
      context.Succeed(requirement);
      return;
    }

    var role = await _accessStore.GetRoleOrNullAsync(
        tenantId,
        accessContext.User.GitHubUserId,
        httpContext.RequestAborted);
    if (role is not null && role.Value >= requirement.MinimumRole)
    {
      context.Succeed(requirement);
    }
  }
}

[DoNotAutoRegister]
internal sealed class SystemAdministratorAuthorizationHandler(
    AccessContextService _accessContextService) :
    AuthorizationHandler<SystemAdministratorRequirement>
{
  protected override async Task HandleRequirementAsync(
      AuthorizationHandlerContext context,
      SystemAdministratorRequirement requirement)
  {
    var cancellationToken = context.Resource is HttpContext httpContext
        ? httpContext.RequestAborted
        : CancellationToken.None;
    var accessContext = await _accessContextService.GetOrNullAsync(
        context.User,
        cancellationToken);
    if (accessContext?.IsSystemAdministrator == true)
    {
      context.Succeed(requirement);
    }
  }
}
