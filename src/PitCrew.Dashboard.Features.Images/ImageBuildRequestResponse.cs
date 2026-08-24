namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns bounded durable state for one trusted image build request.
/// </summary>
public sealed record ImageBuildRequestResponse(
    Guid RequestId,
    Guid RegistrationId,
    int RegistrationVersion,
    string RecipeId,
    string SourceRepository,
    string SourceRef,
    string SourceCommit,
    string Status,
    string? GitHubRunId,
    string? GitHubRunApiUrl,
    string? GitHubRunHtmlUrl,
    string? TerminalCategory,
    string? TerminalDetail,
    DateTimeOffset RequestedAt,
    DateTimeOffset UpdatedAt);
