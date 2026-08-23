namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Carries one bounded GitHub transport outcome without exposing credentials or raw response bodies.
/// </summary>
/// <typeparam name="T">Validated success value type.</typeparam>
/// <param name="Kind">Closed outcome category.</param>
/// <param name="Value">Validated value when <paramref name="Kind"/> is <see cref="GitHubClientOutcomeKind.Success"/>.</param>
/// <param name="RetryAt">Bounded retry time when GitHub supplied rate-limit evidence.</param>
/// <param name="Detail">Bounded non-secret diagnostic category detail.</param>
public sealed record GitHubClientOutcome<T>(
    GitHubClientOutcomeKind Kind,
    T? Value,
    DateTimeOffset? RetryAt,
    string? Detail);
