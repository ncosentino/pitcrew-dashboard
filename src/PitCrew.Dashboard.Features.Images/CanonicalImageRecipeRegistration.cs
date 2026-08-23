namespace PitCrew.Dashboard.Features.Images;

internal sealed record CanonicalImageRecipeRegistration(
    Guid RegistrationId,
    long GitHubInstallationId,
    long GitHubRepositoryId,
    long GitHubWorkflowId,
    string WorkflowPath,
    string DispatchRef,
    string RecipeId,
    int CandidateSchemaVersion,
    string SourceRefPolicyJson,
    string InputSchemaJson,
    IReadOnlyList<string> AllowedSourceRefs,
    IReadOnlyList<ImageRecipeInputDefinition> Inputs);
