namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Result of the pre-candidate durable replay probe. Callers must resolve
/// this result before loading a candidate so that exact replays remain
/// answerable even after candidate retention removes the original
/// immutable candidate row.
/// </summary>
/// <param name="Outcome">
/// The lookup outcome.
/// </param>
/// <param name="CommandId">
/// The durable command identifier when <paramref name="Outcome"/> is
/// <see cref="ImageRolloutIdempotencyLookupOutcome.IdempotentReplay"/>;
/// otherwise <see langword="null"/>. Conflict results never expose the
/// prior command identifier.
/// </param>
public sealed record ImageRolloutIdempotencyLookup(
    ImageRolloutIdempotencyLookupOutcome Outcome,
    Guid? CommandId);
