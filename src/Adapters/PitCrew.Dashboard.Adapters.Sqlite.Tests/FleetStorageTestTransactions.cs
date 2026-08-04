using Microsoft.Data.Sqlite;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

/// <summary>
/// Shares the enlisted-transaction plumbing that fleet and history store tests both need.
/// </summary>
internal static class FleetStorageTestTransactions
{
  public static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);

  public static async Task AppendAsync(
      SqliteFleetHistoryStore store,
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      IReadOnlyList<ManagerObservedState> profiles,
      DateTimeOffset receivedAt,
      HistoryRetentionPolicy retention,
      CancellationToken cancellationToken) =>
      await AppendAsync(
          store,
          connectionFactory,
          nodeId,
          profiles,
          receivedAt,
          new HistoryAppendPolicy(retention, ClockSkewTolerance),
          cancellationToken);

  public static async Task AppendAsync(
      SqliteFleetHistoryStore store,
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      IReadOnlyList<ManagerObservedState> profiles,
      DateTimeOffset receivedAt,
      HistoryAppendPolicy policy,
      CancellationToken cancellationToken)
  {
    await using var transaction = await SqliteFleetTransaction.BeginAsync(
        connectionFactory,
        cancellationToken);
    await store.AppendAsync(
        transaction,
        nodeId,
        profiles,
        receivedAt,
        policy,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  public static async Task ApplySyncAsync(
      SqliteFleetStore store,
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      string connectorVersion,
      DateTimeOffset acceptedAt,
      IReadOnlyList<ManagerObservedState> profiles,
      ConnectorCredentialUpdate credentialUpdate,
      CancellationToken cancellationToken)
  {
    await using var transaction = await SqliteFleetTransaction.BeginAsync(
        connectionFactory,
        cancellationToken);
    await store.ApplySyncAsync(
        transaction,
        nodeId,
        connectorVersion,
        acceptedAt,
        profiles,
        credentialUpdate,
        cancellationToken);
    await store.ApplyHostHardwareAsync(
        transaction,
        nodeId,
        profiles,
        profiles.Select(profile => profile.ProfileId).ToArray(),
        acceptedAt,
        cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  internal static async Task ApplyAuthoritativeSyncAsync(
      SqliteFleetStore store,
      SqliteFleetHistoryStore historyStore,
      SqliteConnectionFactory connectionFactory,
      Guid nodeId,
      string connectorVersion,
      DateTimeOffset acceptedAt,
      IReadOnlyList<ManagerObservedState> profiles,
      ConnectorCredentialUpdate credentialUpdate,
      HistoryAppendPolicy historyPolicy,
      CancellationToken cancellationToken)
  {
    await using var transaction = await SqliteFleetTransaction.BeginAsync(
        connectionFactory,
        cancellationToken);
    await store.ApplySyncAsync(
        transaction,
        nodeId,
        connectorVersion,
        acceptedAt,
        profiles,
        credentialUpdate,
        cancellationToken);
    var acceptedProfileIds = await historyStore.AppendAsync(
        transaction,
        nodeId,
        profiles,
        acceptedAt,
        historyPolicy,
        cancellationToken);
    var hardwareUpdated = false;
    if (profiles.Count == 0)
    {
      await store.ApplyHostHardwareAsync(
          transaction,
          nodeId,
          [],
          [],
          acceptedAt,
          cancellationToken);
      hardwareUpdated = true;
    }
    else if (acceptedProfileIds.Count > 0)
    {
      await store.ApplyHostHardwareAsync(
          transaction,
          nodeId,
          profiles
              .Where(profile =>
                  acceptedProfileIds.Contains(profile.ProfileId))
              .ToArray(),
          profiles.Select(profile => profile.ProfileId).ToArray(),
          acceptedAt,
          cancellationToken);
      hardwareUpdated = true;
    }
    if (hardwareUpdated)
    {
      await historyStore.EnforceRetentionAsync(
          transaction,
          nodeId,
          acceptedAt,
          historyPolicy.Retention,
          cancellationToken);
    }
    await transaction.CommitAsync(cancellationToken);
  }
}
