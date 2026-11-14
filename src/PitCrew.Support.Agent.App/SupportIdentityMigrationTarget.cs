namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityMigrationTarget(
    string DirectoryPath,
    SupportNodeKeyDescriptor Keys,
    string TransportCredential);
