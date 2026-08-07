using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Abstractions;

/// <summary>
/// Bounds retained retrospective connector-health evidence.
/// </summary>
public sealed record ConnectorHealthRetentionPolicy(
    TimeSpan MaximumAge,
    int MaximumEventsPerNode);

/// <summary>
/// Returns the latest connector-health state and its retained event history.
/// </summary>
public sealed record ConnectorHealthProjection(
    ConnectorHealthReplaySnapshot Snapshot,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<ConnectorHealthReplayEvent> Events,
    bool HistoryTruncated);

/// <summary>
/// Associates one node with its latest retained connector-health snapshot.
/// </summary>
public sealed record ConnectorHealthNodeCurrent(
    Guid NodeId,
    ConnectorHealthReplaySnapshot Snapshot,
    DateTimeOffset ReceivedAt);

/// <summary>
/// Persists and reads bounded retrospective connector-health evidence.
/// </summary>
public interface IConnectorHealthStore
{
  /// <summary>
  /// Atomically applies one replay envelope inside the connector synchronization transaction.
  /// </summary>
  Task ApplyAsync(
      IFleetStorageTransaction transaction,
      Guid nodeId,
      ConnectorHealthReplay replay,
      DateTimeOffset receivedAt,
      ConnectorHealthRetentionPolicy retention,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads one tenant-authorized node projection and newest retained events.
  /// </summary>
  Task<ConnectorHealthProjection?> GetAsync(
      string tenantId,
      Guid nodeId,
      int maximumEvents,
      CancellationToken cancellationToken);
}
