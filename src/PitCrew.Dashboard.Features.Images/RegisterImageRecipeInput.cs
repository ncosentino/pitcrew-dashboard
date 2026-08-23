namespace PitCrew.Dashboard.Features.Images;

internal sealed record RegisterImageRecipeInput(
    Guid RegistrationId,
    string GitHubInstallationId,
    string GitHubRepositoryId,
    string GitHubWorkflowId,
    string WorkflowPath,
    string DispatchRef,
    string RecipeId,
    int CandidateSchemaVersion,
    IReadOnlyList<string> AllowedSourceRefs,
    IReadOnlyList<ImageRecipeInputDefinition> Inputs);
