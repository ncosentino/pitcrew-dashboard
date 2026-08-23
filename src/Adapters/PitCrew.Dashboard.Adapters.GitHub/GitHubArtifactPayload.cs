namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubArtifactPayload(
    long Id,
    string? Name,
    long SizeInBytes,
    string? Digest,
    bool Expired,
    DateTimeOffset ExpiresAt,
    string? ArchiveDownloadUrl,
    GitHubArtifactWorkflowRunPayload? WorkflowRun);
