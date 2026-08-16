using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed record SupportEnrollmentCompletionData(
    Guid NodeId,
    string DisplayName,
    SupportEnvelope TransportCredentialEnvelope,
    string RelayUrl,
    string DashboardAuthorizationSigningPublicKeySpki,
    string DashboardResultEncryptionPublicKeySpki);
