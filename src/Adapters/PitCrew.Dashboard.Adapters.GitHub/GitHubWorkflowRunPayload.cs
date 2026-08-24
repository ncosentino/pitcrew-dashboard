namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubWorkflowRunPayload(
    long Id,
    long WorkflowId,
    string? HeadSha,
    string? Status,
    string? Conclusion,
    string? Event,
    string? Url,
    string? HtmlUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
