namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies the outcome of a candidate-domain persistence mutation.
/// </summary>
public enum ImageCandidateMutationResult
{
  /// <summary>
  /// The requested mutation was applied.
  /// </summary>
  Succeeded,

  /// <summary>
  /// An exact idempotent replay required no change.
  /// </summary>
  Unchanged,

  /// <summary>
  /// The tenant-scoped target was not found.
  /// </summary>
  NotFound,

  /// <summary>
  /// Existing durable state conflicts with the requested values.
  /// </summary>
  Conflict,

  /// <summary>
  /// The requested lifecycle transition is not allowed.
  /// </summary>
  InvalidTransition,
}
