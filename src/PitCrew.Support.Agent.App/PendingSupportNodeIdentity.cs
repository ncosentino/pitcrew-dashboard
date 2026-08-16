namespace PitCrew.Support.Agent.App;

internal sealed record PendingSupportNodeIdentity(
    string TenantId,
    string DisplayName,
    string DashboardUrl,
    Guid CompletionId,
    SupportNodeKeyDescriptor Keys);
