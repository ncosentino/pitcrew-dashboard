namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Names the durable phases recorded by the local recovery ledger.
/// </summary>
internal static class RecoveryLedgerPhases
{
  /// <summary>
  /// The attempt was durably recorded before the recovery process was invoked.
  /// </summary>
  public const string Started = "started";

  /// <summary>
  /// The attempt reached an immutable terminal state.
  /// </summary>
  public const string Terminal = "terminal";
}
