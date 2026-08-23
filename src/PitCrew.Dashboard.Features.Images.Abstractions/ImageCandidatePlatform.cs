namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies the closed image platforms supported by candidate schema version 1.
/// </summary>
public enum ImageCandidatePlatform
{
  /// <summary>
  /// The <c>linux/amd64</c> platform.
  /// </summary>
  LinuxAmd64,

  /// <summary>
  /// The <c>linux/arm64</c> platform.
  /// </summary>
  LinuxArm64,
}
