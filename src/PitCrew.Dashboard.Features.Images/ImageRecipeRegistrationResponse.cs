using System.Text.Json.Serialization;

namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns one durable trusted image recipe registration.
/// </summary>
/// <param name="RegistrationId">Stable Dashboard registration identity.</param>
/// <param name="Version">Monotonic registration version within the tenant and recipe.</param>
/// <param name="GitHubInstallationId">Positive decimal GitHub App installation identifier.</param>
/// <param name="GitHubRepositoryId">Positive decimal GitHub repository identifier.</param>
/// <param name="GitHubWorkflowId">Positive decimal GitHub workflow identifier.</param>
/// <param name="RepositoryOwner">Canonical GitHub repository owner.</param>
/// <param name="RepositoryName">Canonical GitHub repository name.</param>
/// <param name="WorkflowPath">Canonical repository-relative workflow path.</param>
/// <param name="WorkflowBlobSha">Exact reviewed workflow blob SHA-1.</param>
/// <param name="DispatchRef">Exact reviewed branch or tag.</param>
/// <param name="RecipeId">Closed trusted recipe identifier.</param>
/// <param name="CandidateSchemaVersion">Trusted candidate schema version.</param>
/// <param name="AllowedSourceRefs">Exact allowed source refs.</param>
/// <param name="Inputs">Declared non-secret workflow inputs.</param>
/// <param name="CreatedByGitHubUserId">GitHub user that created the registration.</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="DisabledByGitHubUserId">GitHub user that disabled the registration, when disabled.</param>
/// <param name="DisabledAt">Disable time when disabled.</param>
public sealed record ImageRecipeRegistrationResponse(
    Guid RegistrationId,
    int Version,
    [property: JsonPropertyName("githubInstallationId")] string GitHubInstallationId,
    [property: JsonPropertyName("githubRepositoryId")] string GitHubRepositoryId,
    [property: JsonPropertyName("githubWorkflowId")] string GitHubWorkflowId,
    string RepositoryOwner,
    string RepositoryName,
    string WorkflowPath,
    string WorkflowBlobSha,
    string DispatchRef,
    string RecipeId,
    int CandidateSchemaVersion,
    IReadOnlyList<string> AllowedSourceRefs,
    IReadOnlyList<ImageRecipeInputDefinition> Inputs,
    string CreatedByGitHubUserId,
    DateTimeOffset CreatedAt,
    string? DisabledByGitHubUserId,
    DateTimeOffset? DisabledAt);
