namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Returns one closed qualification outcome without raw workflow evidence.
/// </summary>
/// <param name="Name">Stable schema qualification name.</param>
/// <param name="Status">Stable qualification outcome.</param>
public sealed record ImageCandidateQualificationResponse(
    string Name,
    string Status);
