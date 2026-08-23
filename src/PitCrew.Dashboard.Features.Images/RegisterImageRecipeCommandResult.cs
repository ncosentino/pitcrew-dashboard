using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images;

internal sealed record RegisterImageRecipeCommandResult(
    ImageRecipeRegistrationCommandStatus Status,
    string? Code,
    string? Error,
    ImageRecipeRegistration? Registration,
    DateTimeOffset? RetryAt);
