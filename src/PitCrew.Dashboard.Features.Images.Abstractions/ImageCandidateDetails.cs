namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Carries one tenant-owned candidate with complete qualification and external run evidence.
/// </summary>
/// <param name="Candidate">Immutable ready or failed candidate.</param>
/// <param name="RegistrationId">Frozen recipe registration identity used by the request.</param>
/// <param name="RegistrationVersion">Frozen recipe registration version used by the request.</param>
/// <param name="GitHubRunApiUrl">Exact GitHub workflow run API URL, when available.</param>
/// <param name="GitHubRunUrl">Exact GitHub workflow run presentation URL, when available.</param>
/// <param name="Qualifications">Complete closed qualification evidence.</param>
public sealed record ImageCandidateDetails(
    ImageCandidate Candidate,
    Guid RegistrationId,
    int RegistrationVersion,
    string? GitHubRunApiUrl,
    string? GitHubRunUrl,
    IReadOnlyList<ImageCandidateQualification> Qualifications);
