namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Reports one profile-image rollout resolution attempt result. Either the
/// <see cref="Profile"/> is projected exactly or an error and (optionally) a
/// failure category are returned. Callers must fail closed when
/// <see cref="Profile"/> is <see langword="null"/>.
/// </summary>
internal sealed record ImageRolloutProfileResolution(
    ImageRolloutProfileState? Profile,
    string? Error,
    string? FailureCategory);
