namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Describes one immutable reviewed image recipe registration version.
/// </summary>
/// <param name="TenantId">Tenant that owns the registration.</param>
/// <param name="RegistrationId">Caller-supplied registration idempotency key for this exact frozen registration.</param>
/// <param name="Version">Monotonically increasing registration version within the tenant and recipe.</param>
/// <param name="GitHubInstallationId">GitHub App installation authorized for the repository.</param>
/// <param name="GitHubRepositoryId">GitHub numeric repository identity.</param>
/// <param name="GitHubWorkflowId">GitHub numeric workflow identity.</param>
/// <param name="RepositoryOwner">Canonical repository owner.</param>
/// <param name="RepositoryName">Canonical repository name.</param>
/// <param name="WorkflowPath">Repository-relative reviewed workflow path.</param>
/// <param name="WorkflowBlobSha">Lowercase SHA-1 identity of the reviewed workflow blob.</param>
/// <param name="DispatchRef">Fixed branch or tag used to dispatch the reviewed workflow.</param>
/// <param name="RecipeId">Closed recipe identifier emitted by the candidate report.</param>
/// <param name="CandidateSchemaVersion">Supported PitCrew candidate schema version.</param>
/// <param name="SourceRefPolicyJson">Bounded canonical JSON describing allowed source refs.</param>
/// <param name="InputSchemaJson">Bounded canonical JSON describing allowed non-secret inputs.</param>
/// <param name="CreatedByGitHubUserId">GitHub user that created this version.</param>
/// <param name="CreatedAt">Caller-supplied creation time.</param>
/// <param name="DisabledByGitHubUserId">GitHub user that disabled this version, when disabled.</param>
/// <param name="DisabledAt">Caller-supplied disable time, when disabled.</param>
public sealed record ImageRecipeRegistration(
    string TenantId,
    Guid RegistrationId,
    int Version,
    long GitHubInstallationId,
    long GitHubRepositoryId,
    long GitHubWorkflowId,
    string RepositoryOwner,
    string RepositoryName,
    string WorkflowPath,
    string WorkflowBlobSha,
    string DispatchRef,
    string RecipeId,
    int CandidateSchemaVersion,
    string SourceRefPolicyJson,
    string InputSchemaJson,
    string CreatedByGitHubUserId,
    DateTimeOffset CreatedAt,
    string? DisabledByGitHubUserId,
    DateTimeOffset? DisabledAt);
