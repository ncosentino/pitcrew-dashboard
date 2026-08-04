namespace PitCrew.Protocol;

/// <summary>
/// Validates profile identifiers shared by managers, connectors, and dashboard restrictions.
/// </summary>
public static class PitCrewProfileId
{
  /// <summary>
  /// Returns whether a value satisfies the public lowercase profile identifier contract.
  /// </summary>
  /// <param name="value">Candidate profile identifier.</param>
  /// <returns><see langword="true"/> when the identifier is valid.</returns>
  public static bool IsValid(string? value)
  {
    if (string.IsNullOrEmpty(value) ||
        value.Length is < 1 or > 32 ||
        value[0] is < 'a' or > 'z')
    {
      return false;
    }
    return value.All(character =>
        character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-');
  }
}
