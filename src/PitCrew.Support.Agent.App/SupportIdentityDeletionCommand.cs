namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityDeletionCommand(
    int SchemaVersion,
    string Operation);
