namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies a closed PitCrew image candidate qualification outcome.
/// </summary>
public enum ImageCandidateQualificationStatus
{
  /// <summary>
  /// The qualification completed successfully.
  /// </summary>
  Passed,

  /// <summary>
  /// The qualification completed with trusted failure evidence.
  /// </summary>
  Failed,

  /// <summary>
  /// The qualification could not produce the required evidence.
  /// </summary>
  Unavailable,
}
