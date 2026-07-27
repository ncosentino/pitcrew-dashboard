namespace PitCrew.Connector.Features.Sync;

internal sealed record SetupProcessResult(
    int? ExitCode,
    bool TimedOut);
