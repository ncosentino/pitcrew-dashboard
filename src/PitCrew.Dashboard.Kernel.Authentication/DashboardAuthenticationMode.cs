namespace PitCrew.Dashboard.Kernel.Authentication;

/// <summary>
/// Selects the human authentication mechanism used by the dashboard.
/// </summary>
public enum DashboardAuthenticationMode
{
  /// <summary>
  /// Authenticates operators through a GitHub OAuth App.
  /// </summary>
  GitHub,

  /// <summary>
  /// Authenticates one configured local operator for loopback development only.
  /// </summary>
  Development,
}
