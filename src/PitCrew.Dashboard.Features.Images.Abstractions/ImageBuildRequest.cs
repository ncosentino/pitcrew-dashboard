namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Describes one durable request to build an exact source commit with a reviewed recipe version.
/// </summary>
/// <param name="TenantId">Tenant that owns the request.</param>
/// <param name="RequestId">Globally unique Dashboard request identity.</param>
/// <param name="RegistrationId">Reviewed recipe registration identity.</param>
/// <param name="RegistrationVersion">Reviewed recipe registration version.</param>
/// <param name="RecipeId">Recipe identity frozen onto the request.</param>
/// <param name="SourceRepository">Canonical owner/name source repository.</param>
/// <param name="SourceCommit">Exact lowercase 40-character source commit.</param>
/// <param name="InputValuesJson">Bounded canonical JSON containing validated input values.</param>
/// <param name="InputValuesSha256">Lowercase SHA-256 hash of the canonical input JSON.</param>
/// <param name="RequestedByGitHubUserId">GitHub user that requested the build.</param>
/// <param name="RequestedAt">Caller-supplied request time.</param>
/// <param name="Status">Current monotonic lifecycle status.</param>
/// <param name="GitHubRunId">Exact correlated GitHub workflow run identity, when known.</param>
/// <param name="GitHubRunUrl">Bounded exact GitHub workflow run URL, when known.</param>
/// <param name="TerminalCategory">Bounded closed terminal category, when terminal evidence exists.</param>
/// <param name="TerminalDetail">Bounded terminal evidence detail, when terminal evidence exists.</param>
/// <param name="UpdatedAt">Caller-supplied time of the latest durable state.</param>
/// <param name="SourceRef">Exact allowed source ref used to validate the source commit.</param>
/// <param name="GitHubRunApiUrl">Exact bounded GitHub workflow run API URL, when known.</param>
public sealed record ImageBuildRequest(
    string TenantId,
    Guid RequestId,
    Guid RegistrationId,
    int RegistrationVersion,
    string RecipeId,
    string SourceRepository,
    string SourceCommit,
    string InputValuesJson,
    string InputValuesSha256,
    string RequestedByGitHubUserId,
    DateTimeOffset RequestedAt,
    ImageBuildRequestStatus Status,
    long? GitHubRunId,
    string? GitHubRunUrl,
    string? TerminalCategory,
    string? TerminalDetail,
    DateTimeOffset UpdatedAt,
    string SourceRef = "",
    string? GitHubRunApiUrl = null);
