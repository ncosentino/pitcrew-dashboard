namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityRotationPlan(
    Guid RotationId,
    Guid NodeId,
    string TenantId,
    string DashboardUrl,
    string CurrentTransportCredential,
    string ReplacementTransportCredential,
    string NodeSigningPublicKeySpki,
    string NodeEncryptionPublicKeySpki);
