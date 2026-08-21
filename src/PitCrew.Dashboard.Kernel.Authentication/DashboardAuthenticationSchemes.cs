namespace PitCrew.Dashboard.Kernel.Authentication;

/// <summary>
/// Provides authentication scheme names shared across Dashboard features.
/// </summary>
public static class DashboardAuthenticationSchemes
{
  /// <summary>
  /// Gets the persistent browser cookie authentication scheme name.
  /// </summary>
  public const string Cookie = "PitCrewCookie";

  /// <summary>
  /// Gets the local development authentication scheme name.
  /// </summary>
  public const string Development = "PitCrewDevelopment";

  /// <summary>
  /// Gets the GitHub OAuth authentication scheme name.
  /// </summary>
  public const string GitHub = "GitHub";
}
