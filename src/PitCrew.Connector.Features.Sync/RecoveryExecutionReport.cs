using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Returns everything one recovery attempt must report to the dashboard.
/// </summary>
/// <param name="Progress">Durable start report, or <see langword="null"/> when nothing started.</param>
/// <param name="Outcome">Terminal outcome for the command.</param>
internal sealed record RecoveryExecutionReport(
    RecoveryCommandProgress? Progress,
    RecoveryCommandOutcome Outcome);
