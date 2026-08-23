namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Contains one exact decoded GitHub Actions workflow file snapshot.
/// </summary>
/// <param name="Path">Canonical repository-relative workflow path.</param>
/// <param name="BlobSha">Exact lowercase Git blob SHA-1 identity.</param>
/// <param name="Reference">Exact branch or tag used to resolve the blob.</param>
/// <param name="Content">Validated bounded UTF-8 workflow YAML text.</param>
public sealed record GitHubWorkflowFileContent(
    string Path,
    string BlobSha,
    string Reference,
    string Content);
