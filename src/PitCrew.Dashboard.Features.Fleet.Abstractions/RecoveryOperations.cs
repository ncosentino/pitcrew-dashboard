using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Abstractions;

/// <summary>
/// Identifies the result of queuing one manager-recovery command.
/// </summary>
public enum RecoveryCommandQueueStatus
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
  /// The connector has not advertised recovery support for the profile.
  /// </summary>
  Unsupported,

  /// <summary>
  /// Local connector policy currently disallows recovery for the profile.
  /// </summary>
  NotAllowed,

  /// <summary>
  /// The requested fences no longer match the connector's advertised state.
  /// </summary>
  StaleFence,

  /// <summary>
  /// Another profile operation of any supported type is already active.
  /// </summary>
  Conflict,

  /// <summary>
  /// A recovery command for the profile was requested too recently.
  /// </summary>
  RateLimited,
}

/// <summary>
/// Returns the outcome of one dashboard manager-recovery request.
/// </summary>
/// <param name="Status">Queue result.</param>
/// <param name="CommandId">Queued command identifier when accepted.</param>
public sealed record RecoveryCommandQueueResult(
    RecoveryCommandQueueStatus Status,
    Guid? CommandId);

/// <summary>
/// Carries the expected fences an administrator observed before requesting recovery.
/// </summary>
/// <param name="ExpectedManagerInstanceId">Manager instance that must still own the profile.</param>
/// <param name="ExpectedGeneration">Desired-capacity generation that must still be current.</param>
/// <param name="ExpectedDesiredStateHash">Desired-state hash that must still be current, or <see langword="null"/>.</param>
public sealed record RecoveryCommandFences(
    string ExpectedManagerInstanceId,
    int ExpectedGeneration,
    string? ExpectedDesiredStateHash);

/// <summary>
/// Describes the immutable audit and lifecycle record of one recovery command.
/// </summary>
/// <param name="CommandId">Dashboard-assigned command identifier.</param>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="FailureCategory">Bounded non-success category when terminal; otherwise <see langword="null"/>.</param>
/// <param name="RequestedByGitHubUserId">Immutable requesting administrator.</param>
/// <param name="RequestedAt">Dashboard time when the command was queued.</param>
/// <param name="ExpiresAt">Time after which the command may no longer execute.</param>
/// <param name="DeliveredAt">Dashboard time when the command was last offered.</param>
/// <param name="ClaimedAt">Connector time when the command was durably claimed.</param>
/// <param name="StartedAt">Connector time when execution durably started.</param>
/// <param name="CompletedAt">Time the command reached its immutable terminal state.</param>
/// <param name="BeforeManagerInstanceId">Manager instance evidence before execution.</param>
/// <param name="AfterManagerInstanceId">Manager instance evidence after execution.</param>
/// <param name="ResultMessage">Bounded operator-facing result detail.</param>
public sealed record RecoveryCommandState(
    Guid CommandId,
    string Status,
    string? FailureCategory,
    string RequestedByGitHubUserId,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? BeforeManagerInstanceId,
    string? AfterManagerInstanceId,
    string? ResultMessage);

/// <summary>
/// Describes the recovery control currently advertised for one profile.
/// </summary>
/// <param name="ProfileId">Profile identifier local to the connector.</param>
/// <param name="ManagerContractVersion">Runtime compatibility contract implemented by the manager.</param>
/// <param name="ManagerContractSupported">Whether local recovery supports that contract.</param>
/// <param name="ExpectedManagerInstanceId">Manager instance the connector expects to recover.</param>
/// <param name="DesiredGeneration">Desired-capacity generation advertised by the connector.</param>
/// <param name="DesiredStateHash">Desired-state hash advertised by the connector.</param>
/// <param name="ObservedStateAgeSeconds">Age of the locally readable observed state.</param>
/// <param name="RecoveryAllowed">Whether local policy allows recovery for the profile.</param>
/// <param name="SingleManagerResolved">Whether exactly one running manager is locally resolvable.</param>
/// <param name="OperationActive">Whether another profile operation is active.</param>
/// <param name="LatestCommand">Latest recovery command for this profile, when present.</param>
public sealed record RecoveryControlState(
    string ProfileId,
    int ManagerContractVersion,
    bool ManagerContractSupported,
    string? ExpectedManagerInstanceId,
    int DesiredGeneration,
    string? DesiredStateHash,
    int ObservedStateAgeSeconds,
    bool RecoveryAllowed,
    bool SingleManagerResolved,
    bool OperationActive,
    RecoveryCommandState? LatestCommand);

/// <summary>
/// Groups recovery controls by enrolled node.
/// </summary>
/// <param name="NodeId">Dashboard-assigned node identifier.</param>
/// <param name="Profiles">Profile recovery controls.</param>
public sealed record NodeRecoveryControls(
    Guid NodeId,
    IReadOnlyList<RecoveryControlState> Profiles);

/// <summary>
/// Persists and delivers typed manager-recovery commands with at-most-once semantics.
/// </summary>
public interface IRecoveryCommandStore
{
  /// <summary>
  /// Queues one recovery command after validating ownership, capability, and fences.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the node.</param>
  /// <param name="nodeId">Dashboard-assigned node identifier.</param>
  /// <param name="profileId">Locally advertised profile identifier.</param>
  /// <param name="fences">Fences the administrator observed before requesting recovery.</param>
  /// <param name="requestedByGitHubUserId">Administrator that requested the command.</param>
  /// <param name="requestedAt">Dashboard time when the command was requested.</param>
  /// <param name="expiresAt">Time after which execution is rejected.</param>
  /// <param name="capabilityObservedAfter">Capability older than this time is treated as stale.</param>
  /// <param name="repeatAllowedAfter">Earlier requests newer than this time are rate limited.</param>
  /// <param name="cancellationToken">Token that cancels queueing.</param>
  /// <returns>The queue result.</returns>
  Task<RecoveryCommandQueueResult> QueueAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      RecoveryCommandFences fences,
      string requestedByGitHubUserId,
      DateTimeOffset requestedAt,
      DateTimeOffset expiresAt,
      DateTimeOffset capabilityObservedAfter,
      DateTimeOffset repeatAllowedAfter,
      CancellationToken cancellationToken);

  /// <summary>
  /// Applies connector capability, progress, and outcome state, then offers at most one command.
  /// </summary>
  /// <param name="nodeId">Authenticated node identifier.</param>
  /// <param name="capability">Current local capability, or <see langword="null"/> when disabled.</param>
  /// <param name="progress">Durable claim or start report, or <see langword="null"/>.</param>
  /// <param name="outcome">Terminal command outcome, or <see langword="null"/>.</param>
  /// <param name="receivedAt">Dashboard time when synchronization was accepted.</param>
  /// <param name="redeliverBefore">Unclaimed offers older than this time may be offered again.</param>
  /// <param name="cancellationToken">Token that cancels synchronization.</param>
  /// <returns>A command offered for execution, or <see langword="null"/>.</returns>
  Task<RecoverManagerCommand?> ApplyConnectorSyncAsync(
      Guid nodeId,
      RecoveryOperatorCapability? capability,
      RecoveryCommandProgress? progress,
      RecoveryCommandOutcome? outcome,
      DateTimeOffset receivedAt,
      DateTimeOffset redeliverBefore,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads connector-advertised recovery controls and command state for one tenant.
  /// </summary>
  /// <param name="tenantId">Tenant whose controls should be returned.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>Recovery controls grouped by node.</returns>
  Task<IReadOnlyList<NodeRecoveryControls>> GetControlsAsync(
      string tenantId,
      CancellationToken cancellationToken);
}
