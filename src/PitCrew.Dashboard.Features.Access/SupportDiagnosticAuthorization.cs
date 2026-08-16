using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using NexusLabs.Needlr;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Access;

internal sealed record SupportDiagnosticAccessRequirement : IAuthorizationRequirement;

[DoNotAutoRegister]
internal sealed class SupportDiagnosticAccessAuthorizationHandler(
    AccessContextService _accessContextService,
    IAccessStore _accessStore) :
    AuthorizationHandler<SupportDiagnosticAccessRequirement>
{
  protected override async Task HandleRequirementAsync(
      AuthorizationHandlerContext context,
      SupportDiagnosticAccessRequirement requirement)
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

    if (context.User.Identity?.AuthenticationType == DiagnosticAuthenticationDefaults.Scheme &&
        string.Equals(
            context.User.FindFirst(DiagnosticClaimTypes.TenantId)?.Value,
            tenantId,
            StringComparison.Ordinal) &&
        context.User.HasClaim(DiagnosticClaimTypes.Permission, "diagnostics.read"))
    {
      context.Succeed(requirement);
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
    if (role is not null && role.Value >= TenantRole.Administrator)
    {
      context.Succeed(requirement);
    }
  }
}
