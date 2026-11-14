namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityMigrationState(
    int SchemaVersion,
    Guid RotationId,
    string RequestDigest,
    string Phase);
