namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Carries a caller-bounded artifact metadata page for one exact workflow run.
/// </summary>
/// <param name="TotalCount">Total artifact count reported by GitHub.</param>
/// <param name="Artifacts">Validated metadata containing no more than the caller-supplied limit.</param>
public sealed record GitHubWorkflowArtifactList(
    int TotalCount,
    IReadOnlyList<GitHubWorkflowArtifact> Artifacts);
