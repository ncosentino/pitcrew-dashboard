namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubDispatchResultPayload(
    long WorkflowRunId,
    string? RunUrl,
    string? HtmlUrl);
