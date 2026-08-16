namespace PitCrew.Support.Agent.App;

/// <summary>
/// Safe local support identity status intended for installer and operator tooling.
/// </summary>
/// <param name="Lifecycle">Current local lifecycle.</param>
/// <param name="NodeId">Dashboard node identifier when enrollment completed.</param>
/// <param name="TenantId">Tenant identifier when locally known.</param>
/// <param name="KeyProvider">Platform key provider name.</param>
/// <param name="KeySetId">Opaque local key-set identifier.</param>
/// <param name="NodeSigningPublicKeySpki">Node signing public SPKI.</param>
/// <param name="NodeEncryptionPublicKeySpki">Node encryption public SPKI.</param>
public sealed record SupportNodeIdentityStatus(
    SupportNodeIdentityLifecycle Lifecycle,
    Guid? NodeId,
    string? TenantId,
    string? KeyProvider,
    string? KeySetId,
    string? NodeSigningPublicKeySpki,
    string? NodeEncryptionPublicKeySpki);
