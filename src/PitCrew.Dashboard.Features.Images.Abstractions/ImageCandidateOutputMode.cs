namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies the closed image output modes supported by candidate schema version 1.
/// </summary>
public enum ImageCandidateOutputMode
{
  /// <summary>
  /// The image was published to a registry.
  /// </summary>
  Registry,

  /// <summary>
  /// The image was emitted as OCI output.
  /// </summary>
  Oci,
}
