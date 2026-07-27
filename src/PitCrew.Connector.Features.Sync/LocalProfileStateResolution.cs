namespace PitCrew.Connector.Features.Sync;

internal sealed record LocalProfileStateResolution(
    LocalProfileStateLocation? Location,
    string? Error);
