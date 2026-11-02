using System.Security.Claims;

using PitCrew.Dashboard.Features.Access.Abstractions;

namespace PitCrew.Dashboard.Features.Fleet;

internal static class DiagnosticFleetAccess
{
  public static DiagnosticAccessScope? GetScopeOrNull(
      IDiagnosticAccessScopeAccessor scopeAccessor,
      ClaimsPrincipal principal,
      string tenantId)
  {
    var scope = scopeAccessor.GetOrNull(principal);
    return scope is not null &&
        string.Equals(
            scope.TenantId,
            tenantId,
            StringComparison.Ordinal)
        ? scope
        : null;
  }

  public static bool AllowsNode(
      DiagnosticAccessScope scope,
      Guid nodeId) =>
      scope.NodeIds.Count == 0 ||
      scope.NodeIds.Contains(nodeId);

  public static bool AllowsProfile(
      DiagnosticAccessScope scope,
      string profileId) =>
      scope.ProfileIds.Count == 0 ||
      scope.ProfileIds.Contains(
          profileId,
          StringComparer.Ordinal);

  public static HashSet<string>? CreateProfileRestrictionSet(
      IReadOnlyList<string> profileIds)
  {
    if (profileIds.Count == 0)
    {
      return null;
    }
    // Profile IDs are schema-canonical lowercase identifiers. Ordinal matching
    // rejects malformed case instead of broadening a credential restriction.
#pragma warning disable NLF0016
    return new HashSet<string>(
        profileIds,
        StringComparer.Ordinal);
#pragma warning restore NLF0016
  }
}
