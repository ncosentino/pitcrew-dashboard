namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Describes one exact GitHub workflow run without exposing logs or credentials.
/// </summary>
/// <param name="Id">Exact workflow run identity.</param>
/// <param name="WorkflowId">Exact workflow identity that produced the run.</param>
/// <param name="HeadSha">Exact lowercase source SHA reported by GitHub.</param>
/// <param name="Status">Bounded GitHub run status.</param>
/// <param name="Conclusion">Bounded terminal conclusion, or <see langword="null"/> while active.</param>
/// <param name="RunApiUrl">Exact bounded GitHub API URL.</param>
/// <param name="RunHtmlUrl">Exact bounded GitHub web URL.</param>
/// <param name="CreatedAt">GitHub creation time.</param>
/// <param name="UpdatedAt">GitHub update time.</param>
/// <param name="Event">Bounded event that created the workflow run.</param>
public sealed record GitHubWorkflowRun(
    long Id,
    long WorkflowId,
    string HeadSha,
    string Status,
    string? Conclusion,
    Uri RunApiUrl,
    Uri RunHtmlUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Event = "");
