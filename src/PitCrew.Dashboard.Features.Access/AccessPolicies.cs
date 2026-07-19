namespace PitCrew.Dashboard.Features.Access;

/// <summary>
/// Defines authorization policies used by tenant-scoped dashboard endpoints.
/// </summary>
public static class AccessPolicies
{
  /// <summary>
  /// Requires deployment-wide system administrator access.
  /// </summary>
  public const string SystemAdministrator =
      "PitCrew.SystemAdministrator";

  /// <summary>
  /// Requires viewer access to the tenant in the current route.
  /// </summary>
  public const string TenantViewer = "PitCrew.TenantViewer";

  /// <summary>
  /// Requires administrator access to the tenant in the current route.
  /// </summary>
  public const string TenantAdministrator =
      "PitCrew.TenantAdministrator";

  /// <summary>
  /// Requires owner access to the tenant in the current route.
  /// </summary>
  public const string TenantOwner = "PitCrew.TenantOwner";
}
