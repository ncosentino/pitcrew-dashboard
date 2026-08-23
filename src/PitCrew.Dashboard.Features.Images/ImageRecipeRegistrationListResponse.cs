namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns one bounded page of image recipe registrations.
/// </summary>
/// <param name="Registrations">Returned registration rows.</param>
/// <param name="Truncated">Whether older matching registrations were omitted.</param>
public sealed record ImageRecipeRegistrationListResponse(
    IReadOnlyList<ImageRecipeRegistrationResponse> Registrations,
    bool Truncated);
