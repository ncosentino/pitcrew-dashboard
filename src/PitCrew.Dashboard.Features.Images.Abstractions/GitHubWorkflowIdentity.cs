namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies one exact GitHub Actions workflow and its current state.
/// </summary>
/// <param name="Id">GitHub numeric workflow identity.</param>
/// <param name="Name">Bounded workflow display name.</param>
/// <param name="Path">Repository-relative workflow path.</param>
/// <param name="State">Mapped workflow state.</param>
public sealed record GitHubWorkflowIdentity(
    long Id,
    string Name,
    string Path,
    GitHubWorkflowState State);
