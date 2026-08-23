namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubArtifactListPayload(
    int TotalCount,
    IReadOnlyList<GitHubArtifactPayload>? Artifacts);
