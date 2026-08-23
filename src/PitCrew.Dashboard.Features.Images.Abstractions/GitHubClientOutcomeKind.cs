namespace PitCrew.Dashboard.Features.Images.Abstractions;

/// <summary>
/// Identifies the bounded outcome of one GitHub image-workflow transport operation.
/// </summary>
public enum GitHubClientOutcomeKind
{
  /// <summary>The operation completed and returned a validated value.</summary>
  Success,

  /// <summary>The optional GitHub App integration is disabled or not configured.</summary>
  NotConfigured,

  /// <summary>The exact requested GitHub resource was not found.</summary>
  NotFound,

  /// <summary>The installation is unauthenticated or lacks required authority.</summary>
  UnauthorizedOrForbidden,

  /// <summary>GitHub rejected the operation because a rate limit was reached.</summary>
  RateLimited,

  /// <summary>A retryable transport or GitHub service failure occurred.</summary>
  TransientFailure,

  /// <summary>The caller supplied an invalid or unbounded request.</summary>
  InvalidRequest,

  /// <summary>GitHub returned a malformed, oversized, or contract-incompatible response.</summary>
  InvalidResponse,

  /// <summary>The caller cancelled the operation.</summary>
  Cancelled,

  /// <summary>The configured request timeout elapsed.</summary>
  TimedOut,
}
