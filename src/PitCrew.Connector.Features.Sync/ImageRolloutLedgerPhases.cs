namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Names the durable phases recorded by the local image-rollout ledger.
/// </summary>
internal static class ImageRolloutLedgerPhases
{
  /// <summary>
  /// The attempt was durably recorded before the rollout process was invoked.
  /// </summary>
  public const string Started = "started";

  /// <summary>
  /// The attempt reached an immutable terminal state.
  /// </summary>
  public const string Terminal = "terminal";
}
