using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images;

internal sealed record ImageCandidateArchiveParseOutcome(
    ImageCandidate? Candidate,
    IReadOnlyList<ImageCandidateQualification> Qualifications,
    string? ErrorCode,
    string? ErrorDetail)
{
  public bool Succeeded => Candidate is not null;

  public static ImageCandidateArchiveParseOutcome Success(
      ImageCandidate candidate,
      IReadOnlyList<ImageCandidateQualification> qualifications) =>
      new(candidate, qualifications, null, null);

  public static ImageCandidateArchiveParseOutcome Invalid(
      string errorCode,
      string errorDetail) =>
      new(null, [], errorCode, errorDetail);
}
