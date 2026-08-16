namespace PitCrew.Support.Protocol;

/// <summary>
/// Closed read-only diagnostics supported by support-plane v1.
/// </summary>
public static class SupportDiagnosticModes
{
  /// <summary>Diagnose the normal connector being unreachable or stale.</summary>
  public const string ConnectorOffline = "ConnectorOffline";

  /// <summary>Diagnose configured capacity not matching observed runner capacity.</summary>
  public const string CapacityMismatch = "CapacityMismatch";

  /// <summary>Diagnose an expected GitHub job that has not been assigned locally.</summary>
  public const string JobNotAssigned = "JobNotAssigned";

  /// <summary>Diagnose local host resource pressure evidence.</summary>
  public const string HostPressure = "HostPressure";

  /// <summary>Collect the complete bounded file-only diagnostic report.</summary>
  public const string Full = "Full";

  private static readonly string[] _all =
  [
      ConnectorOffline,
      CapacityMismatch,
      JobNotAssigned,
      HostPressure,
      Full,
  ];

  /// <summary>
  /// Gets every v1 diagnostic mode in display order.
  /// </summary>
  public static IReadOnlyList<string> All => _all;

  /// <summary>
  /// Returns whether the supplied value is one of the closed v1 modes.
  /// </summary>
  /// <param name="value">Candidate diagnostic mode.</param>
  /// <returns><see langword="true" /> when the value is a supported v1 mode.</returns>
  public static bool IsSupported(string value) =>
      _all.Contains(value, StringComparer.Ordinal);
}
