using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Reports the durable progress and terminal outcome of one rollout attempt.
/// </summary>
internal sealed record ImageRolloutExecutionReport(
    ImageRolloutCommandProgress? Progress,
    ImageRolloutCommandOutcome Outcome);
