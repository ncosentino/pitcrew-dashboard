namespace PitCrew.Support.Relay.App;

internal sealed record RelayNodeActivityRequest(
    string TenantId,
    IReadOnlyList<Guid> NodeIds);
