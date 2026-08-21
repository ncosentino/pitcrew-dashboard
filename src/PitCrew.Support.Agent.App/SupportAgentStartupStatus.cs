namespace PitCrew.Support.Agent.App;

internal sealed record SupportAgentStartupStatus(
    int SchemaVersion,
    string Phase,
    string Disposition,
    string? ExceptionType,
    DateTimeOffset OccurredAt);
