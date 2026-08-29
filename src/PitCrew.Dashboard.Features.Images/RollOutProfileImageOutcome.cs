namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Reports one orchestrator-level rollout attempt result. When
/// <see cref="Status"/> is
/// <see cref="RollOutProfileImageStatus.Queued"/> or
/// <see cref="RollOutProfileImageStatus.IdempotentReplay"/> the
/// <see cref="CommandId"/> is populated with the durable command identity.
/// </summary>
internal sealed record RollOutProfileImageOutcome(
    RollOutProfileImageStatus Status,
    Guid? CommandId,
    string TenantId,
    string? Code,
    string? Error);
