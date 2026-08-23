namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Describes bounded metadata for one artifact belonging to an exact workflow run.
/// </summary>
/// <param name="Id">GitHub numeric artifact identity.</param>
/// <param name="WorkflowRunId">Exact associated workflow run identity.</param>
/// <param name="Name">Bounded artifact name.</param>
/// <param name="SizeInBytes">Artifact archive size reported by GitHub.</param>
/// <param name="Digest">Bounded immutable digest when GitHub supplies one.</param>
/// <param name="Expired">Whether GitHub marks the artifact expired.</param>
/// <param name="ExpiresAt">GitHub expiry time.</param>
/// <param name="ArchiveDownloadUrl">Exact bounded API URL for later archive retrieval.</param>
public sealed record GitHubWorkflowArtifact(
    long Id,
    long WorkflowRunId,
    string Name,
    long SizeInBytes,
    string? Digest,
    bool Expired,
    DateTimeOffset ExpiresAt,
    Uri ArchiveDownloadUrl);
