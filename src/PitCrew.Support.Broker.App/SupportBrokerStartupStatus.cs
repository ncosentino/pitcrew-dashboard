namespace PitCrew.Support.Broker.App;

internal sealed record SupportBrokerStartupStatus(
    int SchemaVersion,
    string Disposition,
    DateTimeOffset OccurredAt);
