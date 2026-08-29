using System.Security.Claims;

namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Queues one profile-image rollout command through the fleet store.
/// </summary>
public interface IRollOutProfileImageUnitOfWork
{
  /// <summary>
  /// Queues one command against the requested tenant, node, profile, and
  /// candidate authority. The <paramref name="idempotencyKey"/> is the
  /// bounded stable header the caller supplied; the fleet layer computes
  /// the authority signature and delegates to the store.
  /// </summary>
  /// <returns>The queue result, or <see langword="null"/> when the principal
  /// is not an authenticated dashboard user.</returns>
  Task<ImageRolloutCommandQueueResult?> QueueOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      ImageRolloutCandidateAuthority candidate,
      ImageRolloutCommandFences fences,
      string idempotencyKey,
      CancellationToken cancellationToken);

  /// <summary>
  /// Probes whether one exact rollout command already exists for the
  /// current caller before candidate lookup or eligibility. Callers use
  /// this to preserve durable at-most-once semantics when a prior
  /// candidate row has since been removed by retention.
  /// </summary>
  /// <remarks>
  /// The fleet layer computes the authority signature from the caller's
  /// <paramref name="candidateId"/> plus every fence. The signature must
  /// never include recipe, digest, or platform values because those are
  /// immutably derived from the candidate and cannot change for one
  /// candidate identifier. This keeps the signature computable at the
  /// pre-candidate-lookup boundary while still distinguishing conflicting
  /// requests that reuse the same key with different candidate or fence
  /// authority.
  /// </remarks>
  /// <returns>The lookup outcome, or <see langword="null"/> when the
  /// principal is not an authenticated dashboard user.</returns>
  Task<ImageRolloutIdempotencyLookup?> LookupReplayOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      Guid candidateId,
      ImageRolloutCommandFences fences,
      string idempotencyKey,
      CancellationToken cancellationToken);
}
