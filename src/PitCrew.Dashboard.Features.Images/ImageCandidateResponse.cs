namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns bounded immutable candidate identity, outcome, and qualification evidence.
/// </summary>
/// <param name="CandidateId">Candidate identity.</param>
/// <param name="RequestId">Authorizing build request identity.</param>
/// <param name="RegistrationId">Frozen recipe registration identity.</param>
/// <param name="RegistrationVersion">Frozen recipe registration version.</param>
/// <param name="Outcome">Ready or failed terminal outcome.</param>
/// <param name="RecipeId">Reported recipe identity.</param>
/// <param name="SourceRepository">Canonical source repository.</param>
/// <param name="SourceCommit">Exact source commit.</param>
/// <param name="GitHubRunId">Exact GitHub workflow run identity.</param>
/// <param name="GitHubRunApiUrl">Exact workflow run API URL, when available.</param>
/// <param name="GitHubRunUrl">Exact workflow run presentation URL, when available.</param>
/// <param name="ArtifactId">Exact GitHub artifact identity.</param>
/// <param name="ArtifactName">Fixed candidate artifact name.</param>
/// <param name="ArtifactDigest">Verified artifact archive digest.</param>
/// <param name="ReportHash">Verified candidate report hash.</param>
/// <param name="ImageReference">Reported tagged image reference.</param>
/// <param name="Digest">Immutable image digest, when available.</param>
/// <param name="ImmutableReference">Digest-qualified registry reference, when available.</param>
/// <param name="Platform">Closed candidate platform.</param>
/// <param name="OutputMode">Closed candidate output mode.</param>
/// <param name="FailureCategory">Closed failure category for failed candidates.</param>
/// <param name="FailureDetail">Closed failure detail for failed candidates.</param>
/// <param name="CreatedAt">Candidate creation time reported by the workflow.</param>
/// <param name="StoredAt">Dashboard persistence time.</param>
/// <param name="Qualifications">Complete closed qualification evidence.</param>
public sealed record ImageCandidateResponse(
    Guid CandidateId,
    Guid RequestId,
    Guid RegistrationId,
    int RegistrationVersion,
    string Outcome,
    string RecipeId,
    string SourceRepository,
    string SourceCommit,
    string GitHubRunId,
    string? GitHubRunApiUrl,
    string? GitHubRunUrl,
    string ArtifactId,
    string ArtifactName,
    string ArtifactDigest,
    string ReportHash,
    string ImageReference,
    string? Digest,
    string? ImmutableReference,
    string Platform,
    string OutputMode,
    string? FailureCategory,
    string? FailureDetail,
    DateTimeOffset CreatedAt,
    DateTimeOffset StoredAt,
    IReadOnlyList<ImageCandidateQualificationResponse> Qualifications);
