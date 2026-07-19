namespace PitCrew.Dashboard.Kernel.DisplayNames;

/// <summary>
/// Defines the shared contract for operator-facing tenant and server names.
/// </summary>
public static class OperatorDisplayName
{
  /// <summary>
  /// Gets the maximum normalized display-name length.
  /// </summary>
  public const int MaximumLength = 128;

  /// <summary>
  /// Trims and validates an operator-facing display name.
  /// </summary>
  /// <param name="value">Candidate display name.</param>
  /// <returns>The normalized name, or <see langword="null"/> when invalid.</returns>
  public static string? NormalizeOrNull(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }
    var normalized = value.Trim();
    return normalized.Length <= MaximumLength
        ? normalized
        : null;
  }
}
