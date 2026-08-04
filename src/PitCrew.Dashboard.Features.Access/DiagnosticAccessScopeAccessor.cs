using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;

namespace PitCrew.Dashboard.Features.Access;

/// <summary>
/// Reads the validated diagnostic scope carried by one authenticated principal.
/// </summary>
public interface IDiagnosticAccessScopeAccessor
{
  /// <summary>
  /// Returns the diagnostic scope or <see langword="null"/> for another authentication scheme.
  /// </summary>
  /// <param name="principal">Authenticated request principal.</param>
  /// <returns>The parsed diagnostic scope.</returns>
  DiagnosticAccessScope? GetOrNull(ClaimsPrincipal principal);
}

internal sealed class DiagnosticAccessScopeAccessor :
    IDiagnosticAccessScopeAccessor
{
  public DiagnosticAccessScope? GetOrNull(ClaimsPrincipal principal)
  {
    if (principal.Identity?.AuthenticationType !=
        DiagnosticAuthenticationDefaults.Scheme)
    {
      return null;
    }
    var credentialValue = principal.FindFirstValue(
        ClaimTypes.NameIdentifier);
    var tenantId = principal.FindFirstValue(
        DiagnosticClaimTypes.TenantId);
    if (!Guid.TryParseExact(
            credentialValue,
            "N",
            out var credentialId) ||
        string.IsNullOrWhiteSpace(tenantId))
    {
      return null;
    }
    var nodes = principal.FindAll(DiagnosticClaimTypes.NodeId)
        .Select(claim => Guid.TryParseExact(
            claim.Value,
            "N",
            out var nodeId)
            ? nodeId
            : Guid.Empty)
        .Where(nodeId => nodeId != Guid.Empty)
        .Distinct()
        .Order()
        .ToArray();
    var profiles = principal.FindAll(
            DiagnosticClaimTypes.ProfileId)
        .Select(claim => claim.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
    return new DiagnosticAccessScope(
        credentialId,
        tenantId,
        nodes,
        profiles);
  }
}
