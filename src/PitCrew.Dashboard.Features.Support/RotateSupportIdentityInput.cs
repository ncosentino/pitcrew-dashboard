namespace PitCrew.Dashboard.Features.Support;

internal sealed record RotateSupportIdentityInput(
    Guid RotationId,
    string TenantId,
    Guid NodeId,
    string CurrentTransportCredential,
    string ReplacementTransportCredential,
    string NodeSigningPublicKeySpki,
    string NodeEncryptionPublicKeySpki);

internal sealed record FinalizeSupportIdentityRotationInput(
    Guid RotationId,
    string TenantId,
    Guid NodeId,
    string CurrentTransportCredential);
