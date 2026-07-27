namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Returns one locally resolved recovery state, or the bounded reason it is unavailable.
/// </summary>
/// <param name="Profile">Resolved profile state, or <see langword="null"/>.</param>
/// <param name="Error">Bounded operator-facing reason.</param>
/// <param name="FailureCategory">Bounded protocol failure category.</param>
internal sealed record RecoveryProfileResolution(
    RecoveryProfileState? Profile,
    string? Error,
    string? FailureCategory);
