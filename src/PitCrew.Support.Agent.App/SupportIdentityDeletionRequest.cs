namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityDeletionRequest(
    int SchemaVersion,
    string Operation);
