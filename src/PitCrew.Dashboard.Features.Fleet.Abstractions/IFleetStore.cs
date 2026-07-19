using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Abstractions;

/// <summary>
/// Identifies which stored node credential authenticated a connector.
/// </summary>
public enum ConnectorCredentialSlot
{
  /// <summary>
  /// The connector presented the currently active credential.
  /// </summary>
  Current,

  /// <summary>
  /// The connector presented a staged replacement credential.
  /// </summary>
  Pending,
}

/// <summary>
/// Identifies the node and tenant associated with an authenticated connector credential.
/// </summary>
/// <param name="NodeId">Dashboard-assigned node identifier.</param>
/// <param name="TenantId">Tenant that owns the enrolled node.</param>
/// <param name="CredentialSlot">Stored credential slot that matched the bearer credential.</param>
/// <param name="RotationRequested">Whether an administrator requested credential rotation.</param>
public sealed record ConnectorNodeIdentity(
    Guid NodeId,
    string TenantId,
    ConnectorCredentialSlot CredentialSlot,
    bool RotationRequested);

/// <summary>
/// Selects the credential mutation committed with a connector synchronization.
/// </summary>
public enum ConnectorCredentialUpdateKind
{
  /// <summary>
  /// Leaves stored credentials unchanged.
  /// </summary>
  None,

  /// <summary>
  /// Stages a replacement credential while retaining the current credential.
  /// </summary>
  Stage,

  /// <summary>
  /// Promotes the staged credential and invalidates the previous credential.
  /// </summary>
  Promote,
}

/// <summary>
/// Describes one atomic connector credential update.
/// </summary>
/// <param name="Kind">Credential mutation to commit.</param>
/// <param name="CredentialHash">Replacement credential hash used by stage or promotion.</param>
public sealed record ConnectorCredentialUpdate(
    ConnectorCredentialUpdateKind Kind,
    string CredentialHash);

/// <summary>
/// Describes the outcome of redeeming a one-time connector enrollment code.
/// </summary>
public enum ConnectorEnrollmentStatus
{
  /// <summary>
  /// The code was valid and the connector was enrolled.
  /// </summary>
  Accepted,

  /// <summary>
  /// The code was missing, expired, or already consumed.
  /// </summary>
  InvalidCode,
}

/// <summary>
/// Returns the outcome of one enrollment-code redemption.
/// </summary>
/// <param name="Status">Enrollment result.</param>
/// <param name="NodeId">Enrolled node identifier when accepted.</param>
public sealed record ConnectorEnrollmentCommit(
    ConnectorEnrollmentStatus Status,
    Guid? NodeId);

/// <summary>
/// Describes the outcome of an administrator node mutation.
/// </summary>
public enum NodeMutationStatus
{
  /// <summary>
  /// The requested mutation completed.
  /// </summary>
  Succeeded,

  /// <summary>
  /// The node does not exist in the requested tenant.
  /// </summary>
  NotFound,

  /// <summary>
  /// The node is revoked and cannot rotate its credential.
  /// </summary>
  Revoked,
}

/// <summary>
/// Persists enrolled connectors and their latest manager observations.
/// </summary>
public interface IFleetStore
{
  /// <summary>
  /// Creates one expiring, one-time connector enrollment code.
  /// </summary>
  /// <param name="enrollmentCodeId">Dashboard-assigned code identifier.</param>
  /// <param name="tenantId">Tenant allowed to redeem the code.</param>
  /// <param name="codeHash">One-way hash of the raw code returned to the administrator.</param>
  /// <param name="label">Operator-facing purpose for the code.</param>
  /// <param name="createdByGitHubUserId">GitHub user that created the code.</param>
  /// <param name="createdAt">Dashboard time of creation.</param>
  /// <param name="expiresAt">Time after which redemption must be rejected.</param>
  /// <param name="cancellationToken">Token that cancels creation.</param>
  /// <returns>A task that completes after the code is stored.</returns>
  Task CreateEnrollmentCodeAsync(
      Guid enrollmentCodeId,
      string tenantId,
      string codeHash,
      string label,
      string createdByGitHubUserId,
      DateTimeOffset createdAt,
      DateTimeOffset expiresAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Atomically consumes an enrollment code and enrolls or re-enrolls one connector installation.
  /// </summary>
  /// <param name="codeHash">One-way hash of the presented one-time code.</param>
  /// <param name="connectorInstanceId">Stable connector installation identifier.</param>
  /// <param name="displayName">Operator-facing server name.</param>
  /// <param name="credentialHash">One-way hash of the newly issued node credential.</param>
  /// <param name="redeemedAt">Dashboard time of redemption.</param>
  /// <param name="cancellationToken">Token that cancels enrollment.</param>
  /// <returns>The enrollment result.</returns>
  Task<ConnectorEnrollmentCommit> RedeemEnrollmentCodeAsync(
      string codeHash,
      string connectorInstanceId,
      string displayName,
      string credentialHash,
      DateTimeOffset redeemedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Resolves a node from a one-way credential hash.
  /// </summary>
  /// <param name="credentialHash">Hash of the bearer credential presented by the connector.</param>
  /// <param name="cancellationToken">Token that cancels credential lookup.</param>
  /// <returns>The node identity when the credential is valid; otherwise <see langword="null"/>.</returns>
  Task<ConnectorNodeIdentity?> ResolveNodeOrNullAsync(
      string credentialHash,
      CancellationToken cancellationToken);

  /// <summary>
  /// Atomically applies one connector heartbeat and its complete profile projection.
  /// </summary>
  /// <param name="nodeId">Authenticated node identifier.</param>
  /// <param name="connectorVersion">Connector application version.</param>
  /// <param name="receivedAt">Dashboard time when the synchronization was accepted.</param>
  /// <param name="profiles">Latest profile observations visible to the connector.</param>
  /// <param name="credentialUpdate">Credential mutation committed with the snapshot.</param>
  /// <param name="cancellationToken">Token that cancels synchronization.</param>
  /// <returns>A task that completes after the projection is committed.</returns>
  Task ApplySyncAsync(
      Guid nodeId,
      string connectorVersion,
      DateTimeOffset receivedAt,
      IReadOnlyList<ManagerObservedState> profiles,
      ConnectorCredentialUpdate credentialUpdate,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads the latest fleet projection for one tenant.
  /// </summary>
  /// <param name="tenantId">Tenant whose enrolled nodes should be returned.</param>
  /// <param name="generatedAt">Dashboard time used for freshness calculations.</param>
  /// <param name="onlineWindow">Maximum age considered online.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The current fleet response.</returns>
  Task<FleetResponse> GetFleetAsync(
      string tenantId,
      DateTimeOffset generatedAt,
      TimeSpan onlineWindow,
      CancellationToken cancellationToken);

  /// <summary>
  /// Sets the operator-facing display-name override for one tenant node.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the node.</param>
  /// <param name="nodeId">Dashboard-assigned node identifier.</param>
  /// <param name="displayName">New operator-facing server name.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>The mutation status.</returns>
  Task<NodeMutationStatus> RenameNodeAsync(
      string tenantId,
      Guid nodeId,
      string displayName,
      CancellationToken cancellationToken);

  /// <summary>
  /// Revokes one tenant node credential.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the node.</param>
  /// <param name="nodeId">Dashboard-assigned node identifier.</param>
  /// <param name="revokedAt">Dashboard time of revocation.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>The mutation status.</returns>
  Task<NodeMutationStatus> RevokeNodeAsync(
      string tenantId,
      Guid nodeId,
      DateTimeOffset revokedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Requests loss-safe credential rotation on the next connector synchronization.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the node.</param>
  /// <param name="nodeId">Dashboard-assigned node identifier.</param>
  /// <param name="requestedAt">Dashboard time of the request.</param>
  /// <param name="cancellationToken">Token that cancels the mutation.</param>
  /// <returns>The mutation status.</returns>
  Task<NodeMutationStatus> RequestCredentialRotationAsync(
      string tenantId,
      Guid nodeId,
      DateTimeOffset requestedAt,
      CancellationToken cancellationToken);
}
