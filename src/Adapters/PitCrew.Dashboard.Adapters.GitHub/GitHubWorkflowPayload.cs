namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubWorkflowPayload(
    long Id,
    string? Name,
    string? Path,
    string? State);
