namespace PitCrew.Connector.Features.Sync;

internal sealed record SetupProcessRequest(
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);
