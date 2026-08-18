namespace PitCrew.Support.Broker.App;

internal readonly record struct UnixSocketMetadata(
    uint UserId,
    uint GroupId,
    UnixFileMode Mode,
    bool IsSocket);
