namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubRepositoryPayload(
    long Id,
    string? Name,
    GitHubRepositoryOwnerPayload? Owner);
