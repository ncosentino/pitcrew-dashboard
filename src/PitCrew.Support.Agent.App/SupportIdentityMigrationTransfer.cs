namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityMigrationTransfer(
    int SchemaVersion,
    Guid RotationId,
    string RequestDigest,
    SupportIdentityCompletionData Completion,
    IReadOnlyDictionary<string, string> ReplayFiles);
