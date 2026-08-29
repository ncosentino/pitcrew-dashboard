namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns a bounded candidate page.
/// </summary>
/// <param name="Candidates">Newest immutable candidates first.</param>
/// <param name="Truncated">Whether more candidates exist beyond this page.</param>
public sealed record ImageCandidateListResponse(
    IReadOnlyList<ImageCandidateResponse> Candidates,
    bool Truncated);
