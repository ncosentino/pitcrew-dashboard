namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Carries one transactionally leased image build request and its frozen registration authority.
/// </summary>
/// <param name="Request">Leased tenant build request.</param>
/// <param name="Registration">Exact frozen registration version referenced by the request.</param>
/// <param name="LeaseOwner">Bounded worker lease identity.</param>
/// <param name="LeaseExpiresAt">UTC lease expiry.</param>
/// <param name="DispatchSafeToRetry">Whether a prior definitive rate limit proved that no run was accepted.</param>
/// <param name="DispatchAttempts">Durable dispatch attempt count.</param>
/// <param name="PollAttempts">Durable exact-run poll attempt count.</param>
/// <param name="RunNotFoundAttempts">Consecutive exact-run not-found count.</param>
/// <param name="RevisionNotFoundAttempts">Consecutive exact workflow-revision not-found count.</param>
/// <param name="DispatchStartedAt">Latest durable dispatch start time, when dispatch began.</param>
public sealed record ImageBuildExecutionClaim(
    ImageBuildRequest Request,
    ImageRecipeRegistration Registration,
    string LeaseOwner,
    DateTimeOffset LeaseExpiresAt,
    bool DispatchSafeToRetry,
    int DispatchAttempts,
    int PollAttempts,
    int RunNotFoundAttempts,
    int RevisionNotFoundAttempts,
    DateTimeOffset? DispatchStartedAt);
