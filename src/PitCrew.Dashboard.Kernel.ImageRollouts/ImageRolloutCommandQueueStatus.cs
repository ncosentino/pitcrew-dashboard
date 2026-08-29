namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Identifies the result of queuing one profile-image rollout command.
/// </summary>
public enum ImageRolloutCommandQueueStatus
{
  /// <summary>
  /// The command was queued.
  /// </summary>
  Queued,

  /// <summary>
  /// An earlier request with the same idempotency key and identical
  /// authority already produced a durable command; the returned identifier
  /// refers to that existing command.
  /// </summary>
  IdempotentReplay,

  /// <summary>
  /// The idempotency key was reused with different authority. The caller
  /// must pick a fresh key or repeat the identical original request.
  /// </summary>
  IdempotencyKeyReuseConflict,

  /// <summary>
  /// The node does not exist in the requested tenant.
  /// </summary>
  NodeNotFound,

  /// <summary>
  /// The connector has not advertised profile-image rollout for the profile.
  /// </summary>
  Unsupported,

  /// <summary>
  /// Local connector policy currently disallows rollout for the profile in
  /// general (schema disabled, capability not permitted, or a residual
  /// policy category the connector reported).
  /// </summary>
  NotAllowed,

  /// <summary>
  /// The candidate recipe id is not present in the profile's
  /// AllowedRecipeIds set. Distinct from the general
  /// <see cref="NotAllowed"/> so operators can tell a recipe-scoped
  /// rejection from a profile-wide policy rejection.
  /// </summary>
  RecipeNotAllowed,

  /// <summary>
  /// The connector advertised the recipe but its local registry-repository
  /// policy is missing or invalid at execution time. The registry
  /// repository value itself is never surfaced on the wire.
  /// </summary>
  RegistryNotAllowed,

  /// <summary>
  /// The connector's routing/desired-capacity projection cannot describe
  /// the profile safely (unsupported scope, missing identity, malformed
  /// repositories). Distinct from schema/manager unsupported.
  /// </summary>
  UnsupportedTopology,

  /// <summary>
  /// The requested candidate is not tenant-owned, ready, and registry-published.
  /// </summary>
  InvalidCandidate,

  /// <summary>
  /// The candidate architecture does not match the profile architecture.
  /// </summary>
  ArchitectureMismatch,

  /// <summary>
  /// The requested fences no longer match the connector's advertised state.
  /// </summary>
  StaleFence,

  /// <summary>
  /// Another profile operation of any supported type is already active.
  /// </summary>
  Conflict,

  /// <summary>
  /// A rollout command for the profile was requested too recently.
  /// </summary>
  RateLimited,
}
