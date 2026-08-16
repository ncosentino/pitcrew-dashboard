namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityCompletionData(
    Guid NodeId,
    string DisplayName,
    string TransportCredential,
    string RelayUrl,
    string DashboardAuthorizationSigningPublicKeySpki,
    string DashboardResultEncryptionPublicKeySpki);
