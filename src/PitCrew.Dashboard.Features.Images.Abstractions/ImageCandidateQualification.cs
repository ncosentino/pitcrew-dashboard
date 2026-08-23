namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Carries one immutable closed qualification result for a candidate.
/// </summary>
/// <param name="CandidateId">Candidate that owns the qualification.</param>
/// <param name="Name">Closed schema version 1 qualification name.</param>
/// <param name="Status">Closed qualification status.</param>
public sealed record ImageCandidateQualification(
    Guid CandidateId,
    ImageCandidateQualificationName Name,
    ImageCandidateQualificationStatus Status);
