using System.Text.Json.Serialization;

namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Requests registration of one tenant-owned trusted GitHub Actions image recipe.
/// </summary>
/// <param name="RegistrationId">Caller-supplied registration idempotency key.</param>
/// <param name="GitHubInstallationId">Positive decimal GitHub App installation identifier.</param>
/// <param name="GitHubRepositoryId">Positive decimal GitHub repository identifier.</param>
/// <param name="GitHubWorkflowId">Positive decimal GitHub workflow identifier.</param>
/// <param name="WorkflowPath">Exact reviewed repository-relative workflow path.</param>
/// <param name="DispatchRef">Fixed reviewed branch or tag used to resolve the workflow file.</param>
/// <param name="RecipeId">Closed recipe identifier emitted by trusted candidate reports.</param>
/// <param name="CandidateSchemaVersion">Supported PitCrew candidate schema version, currently 1.</param>
/// <param name="AllowedSourceRefs">Exact allowed source refs that later build requests may target.</param>
/// <param name="Inputs">Declared non-secret workflow inputs accepted in addition to reserved Dashboard inputs.</param>
public sealed record RegisterImageRecipeRequest(
    [property: JsonPropertyName("registrationId")] Guid RegistrationId,
    [property: JsonPropertyName("githubInstallationId")] string GitHubInstallationId,
    [property: JsonPropertyName("githubRepositoryId")] string GitHubRepositoryId,
    [property: JsonPropertyName("githubWorkflowId")] string GitHubWorkflowId,
    string WorkflowPath,
    string DispatchRef,
    string RecipeId,
    int CandidateSchemaVersion,
    IReadOnlyList<string>? AllowedSourceRefs,
    IReadOnlyList<ImageRecipeInputDefinition>? Inputs);
