using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Abstractions;

/// <summary>
/// Identifies the result of queuing one capacity command.
/// </summary>
public enum CapacityCommandQueueStatus
{
  /// <summary>
  /// The command was queued.
  /// </summary>
  Queued,

  /// <summary>
  /// The node does not exist in the requested tenant.
  /// </summary>
  NodeNotFound,

  /// <summary>
  /// The connector has not advertised capacity support for the profile.
  /// </summary>
  Unsupported,

  /// <summary>
  /// The requested maximum violates the connector's advertised local policy.
  /// </summary>
  InvalidMaximum,

  /// <summary>
  /// Another capacity command is already active for the profile.
  /// </summary>
  Conflict,
}

/// <summary>
/// Returns the outcome of one dashboard capacity-command request.
/// </summary>
/// <param name="Status">Queue result.</param>
/// <param name="CommandId">Queued command identifier when accepted.</param>
public sealed record CapacityCommandQueueResult(
    CapacityCommandQueueStatus Status,
    Guid? CommandId);

/// <summary>
/// Describes the latest capacity command visible for one profile.
/// </summary>
/// <param name="CommandId">Dashboard-assigned command identifier.</param>
/// <param name="RequestedMaximum">Requested absolute capacity maximum.</param>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="RequestedAt">Dashboard time when the command was queued.</param>
/// <param name="DeliveredAt">Dashboard time when the connector claimed the command.</param>
/// <param name="CompletedAt">Connector-reported completion time.</param>
/// <param name="ResultMessage">Bounded operator-facing result detail.</param>
public sealed record CapacityCommandState(
    Guid CommandId,
    int RequestedMaximum,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CompletedAt,
    string? ResultMessage);

/// <summary>
/// Describes the capacity control currently advertised for one profile.
/// </summary>
/// <param name="ProfileId">Profile identifier local to the connector.</param>
/// <param name="Generation">Current desired-capacity generation.</param>
/// <param name="CurrentMaximum">Current configured maximum.</param>
/// <param name="MaximumAllowed">Local policy ceiling.</param>
/// <param name="LatestCommand">Latest command for this profile, when present.</param>
public sealed record CapacityControlState(
    string ProfileId,
    int Generation,
    int CurrentMaximum,
    int MaximumAllowed,
    CapacityCommandState? LatestCommand);

/// <summary>
/// Groups capacity controls by enrolled node.
/// </summary>
/// <param name="NodeId">Dashboard-assigned node identifier.</param>
/// <param name="Profiles">Profile capacity controls.</param>
public sealed record NodeCapacityControls(
    Guid NodeId,
    IReadOnlyList<CapacityControlState> Profiles);

/// <summary>
/// Persists and delivers typed capacity commands.
/// </summary>
public interface ICapacityCommandStore
{
  /// <summary>
  /// Queues an absolute capacity maximum after validating the latest connector capability.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the node.</param>
  /// <param name="nodeId">Dashboard-assigned node identifier.</param>
  /// <param name="profileId">Locally advertised profile identifier.</param>
  /// <param name="maximum">Requested absolute maximum.</param>
  /// <param name="requestedByGitHubUserId">Administrator that requested the command.</param>
  /// <param name="requestedAt">Dashboard time when the command was requested.</param>
  /// <param name="expiresAt">Time after which delivery is rejected.</param>
  /// <param name="cancellationToken">Token that cancels queueing.</param>
  /// <returns>The queue result.</returns>
  Task<CapacityCommandQueueResult> QueueAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      int maximum,
      string requestedByGitHubUserId,
      DateTimeOffset requestedAt,
      DateTimeOffset expiresAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Applies connector capability and outcome state, then claims at most one command.
  /// </summary>
  /// <param name="nodeId">Authenticated node identifier.</param>
  /// <param name="capability">Current local capability, or <see langword="null"/> when disabled.</param>
  /// <param name="outcome">Previously delivered command outcome, or <see langword="null"/>.</param>
  /// <param name="receivedAt">Dashboard time when synchronization was accepted.</param>
  /// <param name="redeliverBefore">Delivered commands older than this time may be claimed again.</param>
  /// <param name="cancellationToken">Token that cancels synchronization.</param>
  /// <returns>A command claimed for delivery, or <see langword="null"/>.</returns>
  Task<SetCapacityCommand?> ApplyConnectorSyncAsync(
      Guid nodeId,
      CapacityOperatorCapability? capability,
      CapacityCommandOutcome? outcome,
      DateTimeOffset receivedAt,
      DateTimeOffset redeliverBefore,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads connector-advertised controls and command state for one tenant.
  /// </summary>
  /// <param name="tenantId">Tenant whose controls should be returned.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Capacity controls grouped by node.</returns>
  Task<IReadOnlyList<NodeCapacityControls>> GetControlsAsync(
      string tenantId,
      CancellationToken cancellationToken);
}
