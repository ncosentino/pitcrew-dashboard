namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Closed schema constants and predicates enforced when the connector
/// reconstructs a runner-profile manifest from applied static state.
/// </summary>
/// <remarks>
/// Kept intentionally narrow: only the closed literal sets and character
/// patterns that <c>runner-profile.schema.json</c> restricts. Mirrors the
/// public upstream shape without embedding the full schema in production.
/// </remarks>
internal static class ImageRolloutManifestSchema
{
  /// <summary>
  /// Closed literal set of allowed <c>runtime.devices</c> entries. Raw
  /// filesystem device paths (e.g. <c>/dev/kvm</c>) are a Setup-Runner
  /// implementation detail and are rejected here.
  /// </summary>
  internal static readonly string[] AllowedRuntimeDevices = ["kvm"];

  private const int MinimumVolumeNameLength = 2;
  private const int MaximumVolumeNameLength = 64;

  /// <summary>
  /// Validates a Docker volume-name shaped identifier used for
  /// <c>readOnlyVolumes[].name</c> and <c>readOnlyVolumes[].source</c>.
  /// The value must be 2-64 characters, start with an alphanumeric
  /// character, and only contain alphanumerics, <c>_</c>, <c>.</c>, or
  /// <c>-</c>. Slash characters, whitespace, and control characters are
  /// all rejected because the source is a Docker volume name, not a
  /// filesystem path.
  /// </summary>
  internal static bool IsValidVolumeName(string? value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return false;
    }
    if (value.Length < MinimumVolumeNameLength ||
        value.Length > MaximumVolumeNameLength)
    {
      return false;
    }
    if (!IsVolumeNameLeadCharacter(value[0]))
    {
      return false;
    }
    for (var index = 1; index < value.Length; index++)
    {
      if (!IsVolumeNameCharacter(value[index]))
      {
        return false;
      }
    }
    return true;
  }

  private static bool IsVolumeNameLeadCharacter(char c)
  {
    return c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');
  }

  private static bool IsVolumeNameCharacter(char c)
  {
    if (IsVolumeNameLeadCharacter(c))
    {
      return true;
    }
    return c is '_' or '.' or '-';
  }
}
