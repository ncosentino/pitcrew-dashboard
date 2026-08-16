namespace PitCrew.Support.Relay.App;

internal sealed record RelayNodeRegistrationRequest(
    string TenantId,
    Guid NodeId,
    string TransportCredentialHash);

internal sealed record RelaySessionEnqueueRequest(
    string TenantId,
    Guid NodeId,
    Guid SessionId,
    DateTimeOffset ExpiresAt,
    string RequestEnvelope);

internal sealed record RelayResultUploadRequest(string ResultEnvelope);

internal sealed record RelayPollResponse(
    Guid SessionId,
    string RequestEnvelope,
    DateTimeOffset ExpiresAt);

internal sealed record RelaySessionRecord(
    string TenantId,
    Guid NodeId,
    Guid SessionId,
    string Status,
    DateTimeOffset ExpiresAt,
    string RequestEnvelope,
    string? ResultEnvelope);
