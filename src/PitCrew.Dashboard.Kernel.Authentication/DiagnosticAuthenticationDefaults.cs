namespace PitCrew.Dashboard.Kernel.Authentication;

/// <summary>
/// Defines the dedicated authentication boundary for noninteractive diagnostics.
/// </summary>
public static class DiagnosticAuthenticationDefaults
{
  /// <summary>
  /// Gets the ASP.NET Core authentication scheme name.
  /// </summary>
  public const string Scheme = "PitCrewDiagnostics";

  /// <summary>
  /// Gets the HTTP Authorization header scheme.
  /// </summary>
  public const string AuthorizationScheme = "PitCrew-Diagnostics";
}
