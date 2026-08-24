namespace PitCrew.Support.Protocol;

/// <summary>
/// Closed bounded reasons a support agent may report when it does not produce a
/// diagnostic result.
/// </summary>
public static class SupportRequestRejectionDispositions
{
  /// <summary>The envelope version or algorithm is unsupported.</summary>
  public const string EnvelopeUnsupported = "envelope-unsupported";

  /// <summary>The envelope signature is invalid.</summary>
  public const string EnvelopeSignatureRejected =
      "envelope-signature-rejected";

  /// <summary>The envelope payload cannot be decrypted or authenticated.</summary>
  public const string EnvelopePayloadRejected =
      "envelope-payload-rejected";

  /// <summary>The decrypted request is malformed.</summary>
  public const string RequestMalformed = "request-malformed";

  /// <summary>The request names another relay session.</summary>
  public const string SessionMismatch = "session-mismatch";

  /// <summary>The request names another tenant or node.</summary>
  public const string WrongTenantOrNode = "wrong-tenant-or-node";

  /// <summary>The request capability is unsupported.</summary>
  public const string UnsupportedCapability = "unsupported-capability";

  /// <summary>The request diagnostic mode is unsupported.</summary>
  public const string UnsupportedDiagnosticMode =
      "unsupported-diagnostic-mode";

  /// <summary>The signed request authorization expired.</summary>
  public const string RequestExpired = "request-expired";

  /// <summary>The request nonce is invalid.</summary>
  public const string InvalidNonce = "invalid-nonce";

  /// <summary>The request nonce was already observed.</summary>
  public const string RequestReplay = "request-replay";

  /// <summary>A concurrent request owns the replay nonce.</summary>
  public const string ReplayPending = "replay-pending";

  /// <summary>The broker returned unsafe markdown.</summary>
  public const string BrokerMarkdownRejected =
      "broker-markdown-rejected";

  /// <summary>The broker returned an invalid report.</summary>
  public const string BrokerReportRejected =
      "broker-report-rejected";

  /// <summary>The request failed another bounded validation rule.</summary>
  public const string ValidationRejected = "validation-rejected";

  /// <summary>No bounded result was available.</summary>
  public const string ResultUnavailable = "result-unavailable";

  private static readonly string[] _all =
  [
      EnvelopeUnsupported,
      EnvelopeSignatureRejected,
      EnvelopePayloadRejected,
      RequestMalformed,
      SessionMismatch,
      WrongTenantOrNode,
      UnsupportedCapability,
      UnsupportedDiagnosticMode,
      RequestExpired,
      InvalidNonce,
      RequestReplay,
      ReplayPending,
      BrokerMarkdownRejected,
      BrokerReportRejected,
      ValidationRejected,
      ResultUnavailable,
  ];

  /// <summary>
  /// Gets every supported rejection disposition.
  /// </summary>
  public static IReadOnlyList<string> All => _all;

  /// <summary>
  /// Returns whether a value belongs to the closed disposition vocabulary.
  /// </summary>
  /// <param name="value">Candidate disposition.</param>
  /// <returns><see langword="true"/> for a supported disposition.</returns>
  public static bool IsSupported(string value) =>
      _all.Contains(value, StringComparer.Ordinal);
}
