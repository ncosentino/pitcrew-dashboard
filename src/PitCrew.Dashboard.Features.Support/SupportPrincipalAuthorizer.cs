using System.Security.Claims;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Access.Abstractions;

namespace PitCrew.Dashboard.Features.Support;

internal sealed class SupportPrincipalAuthorizer(
    SupportDashboardAccessService _accessContextService,
    IDiagnosticAccessScopeAccessor _diagnosticScopeAccessor)
{
  public async Task<SupportAccessDecision> CanRequestOrReadAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string? profileId,
      CancellationToken cancellationToken)
  {
    var diagnosticScope = _diagnosticScopeAccessor.GetOrNull(principal);
    if (diagnosticScope is not null)
    {
      if (!string.Equals(diagnosticScope.TenantId, tenantId, StringComparison.Ordinal) ||
          diagnosticScope.NodeIds.Count > 0 && !diagnosticScope.NodeIds.Contains(nodeId) ||
          diagnosticScope.ProfileIds.Count > 0 &&
          (profileId is null ||
           !diagnosticScope.ProfileIds.Contains(profileId, StringComparer.Ordinal)))
      {
        return new SupportAccessDecision(false, null, diagnosticScope);
      }
      return new SupportAccessDecision(
          true,
          diagnosticScope.CredentialId.ToString("N"),
          diagnosticScope);
    }

    var context = await _accessContextService.GetOrNullAsync(
        principal,
        cancellationToken);
    if (context is null)
    {
      return new SupportAccessDecision(false, null, null);
    }
    if (await _accessContextService.IsTenantAdministratorAsync(
        context,
        tenantId,
        cancellationToken))
    {
      return new SupportAccessDecision(true, context.User.GitHubUserId, null);
    }
    return new SupportAccessDecision(false, null, null);
  }
}

