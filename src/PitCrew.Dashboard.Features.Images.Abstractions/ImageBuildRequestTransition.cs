namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Carries one optimistic, monotonic image build request transition.
/// </summary>
/// <param name="ExpectedCurrentStatus">Status the caller expects to be durable.</param>
/// <param name="NewStatus">Next requested lifecycle status.</param>
/// <param name="GitHubRunId">Exact GitHub run identity to freeze or replay.</param>
/// <param name="GitHubRunUrl">Bounded exact GitHub run URL to freeze or replay.</param>
/// <param name="TerminalCategory">Bounded closed terminal category for blocked or failed transitions.</param>
/// <param name="TerminalDetail">Bounded terminal detail for blocked or failed transitions.</param>
/// <param name="UpdatedAt">Caller-supplied transition time.</param>
public sealed record ImageBuildRequestTransition(
    ImageBuildRequestStatus ExpectedCurrentStatus,
    ImageBuildRequestStatus NewStatus,
    long? GitHubRunId,
    string? GitHubRunUrl,
    string? TerminalCategory,
    string? TerminalDetail,
    DateTimeOffset UpdatedAt);
