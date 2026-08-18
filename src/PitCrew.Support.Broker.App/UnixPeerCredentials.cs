namespace PitCrew.Support.Broker.App;

internal readonly record struct UnixPeerCredentials(
    int ProcessId,
    uint UserId,
    uint GroupId);
