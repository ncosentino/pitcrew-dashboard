namespace PitCrew.Support.Agent.App;

internal sealed record StoredSupportNodeIdentityManifest(
    int SchemaVersion,
    SupportNodeIdentityLifecycle Lifecycle,
    SupportNodeKeyDescriptor Keys,
    string? TenantId,
    Guid? NodeId,
    string? DisplayName,
    string? DashboardUrl,
    string? RelayUrl,
    string? DashboardAuthorizationSigningPublicKeySpki,
    string? DashboardResultEncryptionPublicKeySpki,
    string ReplayRoot,
    string PipeName,
    Guid? RotationId,
    bool RotationAccepted,
    Guid? EnrollmentCompletionId);
