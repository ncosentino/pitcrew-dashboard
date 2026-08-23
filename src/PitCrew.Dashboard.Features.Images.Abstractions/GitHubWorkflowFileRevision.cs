namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies the exact reviewed workflow file blob at one branch or tag.
/// </summary>
/// <param name="Path">Canonical repository-relative workflow path.</param>
/// <param name="BlobSha">Exact lowercase Git blob SHA-1 identity.</param>
/// <param name="Reference">Exact branch or tag used to resolve the blob.</param>
public sealed record GitHubWorkflowFileRevision(
    string Path,
    string BlobSha,
    string Reference);
