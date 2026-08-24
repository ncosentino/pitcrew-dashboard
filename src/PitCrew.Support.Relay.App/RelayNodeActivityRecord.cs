namespace PitCrew.Support.Relay.App;

internal sealed record RelayNodeActivityRecord(
    Guid NodeId,
    DateTimeOffset? LastPollAt,
    DateTimeOffset? LastResultAt);
