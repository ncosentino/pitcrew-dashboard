namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Acknowledges one accepted profile image rollout with its durable command
/// identity.
/// </summary>
public sealed record RollOutProfileImageResponse(
    Guid CommandId,
    string Status,
    string StatusLocation);
