namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies the monotonic durable lifecycle state of an image build request.
/// </summary>
public enum ImageBuildRequestStatus
{
  /// <summary>
  /// The request is persisted and awaits dispatch.
  /// </summary>
  Requested,

  /// <summary>
  /// The reviewed workflow is being dispatched.
  /// </summary>
  Dispatching,

  /// <summary>
  /// The exact correlated workflow run is building the image.
  /// </summary>
  Building,

  /// <summary>
  /// The terminal workflow artifact is being qualified.
  /// </summary>
  Qualifying,

  /// <summary>
  /// A trusted ready candidate was persisted.
  /// </summary>
  Ready,

  /// <summary>
  /// Policy, identity, artifact, or validation evidence requires administrator intervention.
  /// </summary>
  Blocked,

  /// <summary>
  /// A trusted terminal workflow or candidate failure was persisted.
  /// </summary>
  Failed,
}
