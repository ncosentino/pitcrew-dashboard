namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityMigrationOffer(
    int SchemaVersion,
    Guid NodeId,
    string TenantId,
    string DisplayName,
    string DashboardUrl,
    string RelayUrl,
    string SourceSigningPublicKeySpki,
    string SourceEncryptionPublicKeySpki,
    string DashboardAuthorizationSigningPublicKeySpki,
    string DashboardResultEncryptionPublicKeySpki);
