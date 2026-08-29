namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Identifies the result of looking up whether a durable rollout command
/// already exists for one exact idempotency key.
/// </summary>
public enum ImageRolloutIdempotencyLookupOutcome
{
  /// <summary>
  /// No prior command exists for this <c>(tenant, node, actor, key)</c>
  /// pair. The caller may continue with candidate and eligibility work
  /// and then attempt the atomic queue insertion.
  /// </summary>
  NoExistingCommand,

  /// <summary>
  /// A prior command exists with the same idempotency signature. The
  /// caller must return the durable command identifier as an exact
  /// replay, without repeating candidate lookup or queue work.
  /// </summary>
  IdempotentReplay,

  /// <summary>
  /// A prior command exists with a different idempotency signature. The
  /// caller must surface a stable conflict without exposing the prior
  /// command identifier or authority.
  /// </summary>
  IdempotencyKeyReuseConflict,
}
