namespace PitCrew.Support.Canary.Contracts;

/// <summary>
/// Names the closed request shapes emitted by the run-scoped rejection
/// injector.
/// </summary>
public static class CanaryRejectedRequestCases
{
  /// <summary>A signed envelope whose plaintext is not valid JSON.</summary>
  public const string MalformedRequest = "malformed-request";

  /// <summary>A request bound to another relay session.</summary>
  public const string SessionMismatch = "session-mismatch";

  /// <summary>A request naming another tenant.</summary>
  public const string WrongTenantOrNode = "wrong-tenant-or-node";

  /// <summary>A request naming an unsupported capability.</summary>
  public const string UnsupportedCapability = "unsupported-capability";

  /// <summary>A request naming an unsupported diagnostic mode.</summary>
  public const string UnsupportedDiagnosticMode =
      "unsupported-diagnostic-mode";

  /// <summary>A request whose signed authorization is already expired.</summary>
  public const string ExpiredRequest = "expired-request";

  /// <summary>A request whose nonce violates the bounded nonce contract.</summary>
  public const string InvalidNonce = "invalid-nonce";

  /// <summary>A valid request that seeds one replay nonce.</summary>
  public const string ReplaySeed = "replay-seed";

  /// <summary>A second request that reuses the seeded nonce.</summary>
  public const string RequestReplay = "request-replay";

  private static readonly string[] _all =
  [
      MalformedRequest,
      SessionMismatch,
      WrongTenantOrNode,
      UnsupportedCapability,
      UnsupportedDiagnosticMode,
      ExpiredRequest,
      InvalidNonce,
      ReplaySeed,
      RequestReplay,
  ];

  /// <summary>
  /// Gets every supported injection case.
  /// </summary>
  public static IReadOnlyList<string> All => _all;

  /// <summary>
  /// Returns whether a case belongs to the closed injection vocabulary.
  /// </summary>
  /// <param name="value">Candidate case.</param>
  /// <returns><see langword="true"/> for a supported case.</returns>
  public static bool IsSupported(string value) =>
      _all.Contains(value, StringComparer.Ordinal);
}
