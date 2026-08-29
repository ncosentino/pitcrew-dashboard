namespace PitCrew.Dashboard.Kernel.ImageRollouts;

/// <summary>
/// Returns the outcome of one dashboard profile-image rollout request.
/// </summary>
/// <param name="Status">Queue result.</param>
/// <param name="CommandId">Queued command identifier when accepted.</param>
public sealed record ImageRolloutCommandQueueResult(
    ImageRolloutCommandQueueStatus Status,
    Guid? CommandId);
