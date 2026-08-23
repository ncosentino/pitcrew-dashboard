namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Carries immutable identity and evidence common to ready and failed image candidates.
/// </summary>
/// <param name="CandidateId">Globally unique candidate identity.</param>
/// <param name="TenantId">Tenant that owns the candidate.</param>
/// <param name="RequestId">Build request that authorized the candidate.</param>
/// <param name="RecipeId">Recipe identity reported by the workflow.</param>
/// <param name="SourceRepository">Canonical owner/name source repository.</param>
/// <param name="SourceCommit">Exact lowercase source commit.</param>
/// <param name="GitHubRunId">Exact correlated workflow run identity.</param>
/// <param name="ArtifactId">Exact GitHub artifact identity.</param>
/// <param name="ArtifactName">Expected bounded artifact name.</param>
/// <param name="ArtifactDigest">Lowercase SHA-256 artifact archive digest.</param>
/// <param name="ReportHash">Lowercase SHA-256 hash of the validated report JSON.</param>
/// <param name="ReportJson">Bounded validated candidate report JSON.</param>
/// <param name="ImageReference">Mutable/tagged image reference reported by the workflow.</param>
/// <param name="Platform">Closed candidate platform.</param>
/// <param name="OutputMode">Closed candidate output mode.</param>
/// <param name="CreatedAt">Candidate creation time reported by the workflow.</param>
/// <param name="StoredAt">Caller-supplied Dashboard persistence time.</param>
public abstract record ImageCandidate(
    Guid CandidateId,
    string TenantId,
    Guid RequestId,
    string RecipeId,
    string SourceRepository,
    string SourceCommit,
    long GitHubRunId,
    long ArtifactId,
    string ArtifactName,
    string ArtifactDigest,
    string ReportHash,
    string ReportJson,
    string ImageReference,
    ImageCandidatePlatform Platform,
    ImageCandidateOutputMode OutputMode,
    DateTimeOffset CreatedAt,
    DateTimeOffset StoredAt);
