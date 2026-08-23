namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Carries the exact workflow-run identity returned by the pinned dispatch API.
/// </summary>
/// <param name="RunId">Exact GitHub workflow run identity.</param>
/// <param name="RunApiUrl">Exact bounded GitHub API URL for the run.</param>
/// <param name="RunHtmlUrl">Exact bounded GitHub web URL for the run.</param>
public sealed record GitHubWorkflowDispatch(
    long RunId,
    Uri RunApiUrl,
    Uri RunHtmlUrl);
