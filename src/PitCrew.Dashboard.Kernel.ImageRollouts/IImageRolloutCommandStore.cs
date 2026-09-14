using PitCrew.Protocol;

namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Persists and delivers typed profile-image rollout commands with
/// at-most-once semantics.
/// </summary>
public interface IImageRolloutCommandStore
{
  /// <summary>
  /// Queues one profile-image rollout command after validating ownership,
  /// capability, recipe allowlist, architecture, and fences.
  /// </summary>
  /// <remarks>
  /// The <paramref name="idempotencyKey"/> is bounded caller-supplied
  /// header text and <paramref name="idempotencySignature"/> is a stable
  /// hash of every wire authority value. Repeating the exact same key with
  /// the exact same signature returns the same durable command even during
  /// cooldown or while it is active. Reusing the key with a different
  /// signature returns <see cref="ImageRolloutCommandQueueStatus.IdempotencyKeyReuseConflict"/>.
  /// </remarks>
  Task<ImageRolloutCommandQueueResult> QueueAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      ImageRolloutCandidateAuthority candidate,
      ImageRolloutCommandFences fences,
      string requestedByGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      DateTimeOffset requestedAt,
      DateTimeOffset expiresAt,
      DateTimeOffset capabilityObservedAfter,
      DateTimeOffset repeatAllowedAfter,
      CancellationToken cancellationToken);

  /// <summary>
  /// Looks up whether one exact rollout command already exists for the
  /// <c>(tenant, node, actor, idempotency-key)</c> pair without touching
  /// candidate or fence eligibility. Callers use this probe before
  /// candidate resolution so that an exact replay still returns the
  /// durable command even after candidate retention removes the immutable
  /// candidate row.
  /// </summary>
  /// <remarks>
  /// The lookup is tenant- and revocation-scoped through the nodes join.
  /// It never persists state and never mutates the row. Matching keys
  /// with an equal <paramref name="idempotencySignature"/> resolve to
  /// <see cref="ImageRolloutIdempotencyLookupOutcome.IdempotentReplay"/>;
  /// a different signature resolves to
  /// <see cref="ImageRolloutIdempotencyLookupOutcome.IdempotencyKeyReuseConflict"/>
  /// without leaking the prior command identifier. The atomic queue
  /// insertion in <see cref="QueueAsync"/> repeats this lookup as a race
  /// safeguard.
  /// </remarks>
  Task<ImageRolloutIdempotencyLookup> LookupIdempotentReplayAsync(
      string tenantId,
      Guid nodeId,
      string requestedByGitHubUserId,
      string idempotencyKey,
      string idempotencySignature,
      CancellationToken cancellationToken);

  /// <summary>
  /// Applies connector capability, progress, and outcome state, then offers
  /// at most one queued command.
  /// </summary>
  /// <param name="nodeId">Authenticated node identifier.</param>
  /// <param name="capability">Current local capability, including ignored replay payloads.</param>
  /// <param name="progress">Durable claim or start report, or <see langword="null"/>.</param>
  /// <param name="outcome">Terminal command outcome, or <see langword="null"/>.</param>
  /// <param name="receivedAt">Dashboard time when synchronization was accepted.</param>
  /// <param name="redeliverBefore">Unclaimed offers older than this time may be offered again.</param>
  /// <param name="cancellationToken">Token that cancels synchronization.</param>
  /// <param name="applyCapability">Whether the capability belongs to an accepted complete inventory.</param>
  /// <returns>A command offered for execution, or <see langword="null"/>.</returns>
  Task<RollOutProfileImageCommand?> ApplyConnectorSyncAsync(
      Guid nodeId,
      ImageRolloutOperatorCapability? capability,
      ImageRolloutCommandProgress? progress,
      ImageRolloutCommandOutcome? outcome,
      DateTimeOffset receivedAt,
      DateTimeOffset redeliverBefore,
      CancellationToken cancellationToken,
      bool applyCapability = true);

  /// <summary>
  /// Loads bounded rollout controls for one tenant, newest history first.
  /// </summary>
  Task<IReadOnlyList<NodeImageRolloutControls>> GetControlsAsync(
      string tenantId,
      int observedStateMaximumAgeSeconds,
      CancellationToken cancellationToken,
      int historyPerProfile = 20);

  /// <summary>
  /// Loads one bounded rollout control by node and profile identity.
  /// </summary>
  Task<ImageRolloutControlState?> GetProfileControlOrNullAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      int observedStateMaximumAgeSeconds,
      CancellationToken cancellationToken,
      int historyPerProfile = 20);
}
