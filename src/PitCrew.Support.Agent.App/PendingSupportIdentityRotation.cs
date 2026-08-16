namespace PitCrew.Support.Agent.App;

internal sealed record PendingSupportIdentityRotation(
    Guid RotationId,
    Guid NodeId,
    string TenantId,
    string DashboardUrl,
    string CurrentTransportCredential);
