namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubInstallationTokenReplyPayload(
    string? Token,
    DateTimeOffset ExpiresAt);
