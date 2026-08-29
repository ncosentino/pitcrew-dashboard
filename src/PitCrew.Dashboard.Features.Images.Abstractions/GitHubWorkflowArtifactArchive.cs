namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Carries one bounded archive downloaded for an exact GitHub workflow artifact.
/// </summary>
/// <param name="ArtifactId">Exact GitHub artifact identity.</param>
/// <param name="Content">Bounded archive bytes. Callers must not retain them after validation.</param>
public sealed record GitHubWorkflowArtifactArchive(
    long ArtifactId,
    ReadOnlyMemory<byte> Content);
