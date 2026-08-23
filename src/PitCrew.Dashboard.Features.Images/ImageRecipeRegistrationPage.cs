using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images;

internal sealed record ImageRecipeRegistrationPage(
    IReadOnlyList<ImageRecipeRegistration> Registrations,
    bool Truncated);
