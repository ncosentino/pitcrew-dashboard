namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns one bounded page of tenant image build requests.
/// </summary>
public sealed record ImageBuildRequestListResponse(
    IReadOnlyList<ImageBuildRequestResponse> Requests,
    bool Truncated);
