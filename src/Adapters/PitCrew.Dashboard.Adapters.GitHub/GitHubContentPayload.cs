namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubContentPayload(
    string? Type,
    string? Path,
    string? Sha,
    long Size,
    string? Encoding,
    string? Content);
