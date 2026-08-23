namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Reports whether an exact source commit is reachable from an allowed source ref.
/// </summary>
/// <param name="IsReachable">Whether the allowed ref contains the exact source commit.</param>
public sealed record GitHubCommitReachability(bool IsReachable);
