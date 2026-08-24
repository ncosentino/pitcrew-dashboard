using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images;

internal sealed record ImageBuildRequestCommandResult(
    ImageBuildRequestCommandStatus Status,
    string? Code,
    string? Error,
    ImageBuildRequest? Request,
    DateTimeOffset? RetryAt);
