namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies a closed PitCrew image candidate schema version 1 qualification.
/// </summary>
public enum ImageCandidateQualificationName
{
  /// <summary>
  /// The <c>image-build</c> qualification.
  /// </summary>
  ImageBuild,

  /// <summary>
  /// The <c>buildkit-digest</c> qualification.
  /// </summary>
  BuildKitDigest,

  /// <summary>
  /// The <c>registry-digest</c> qualification.
  /// </summary>
  RegistryDigest,

  /// <summary>
  /// The <c>oci-manifest</c> qualification.
  /// </summary>
  OciManifest,

  /// <summary>
  /// The <c>builder-cleanup</c> qualification.
  /// </summary>
  BuilderCleanup,
}
