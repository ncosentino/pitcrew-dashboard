namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubInstallationTokenPayload(
    IReadOnlyList<long> RepositoryIds,
    GitHubInstallationTokenPermissions Permissions);
