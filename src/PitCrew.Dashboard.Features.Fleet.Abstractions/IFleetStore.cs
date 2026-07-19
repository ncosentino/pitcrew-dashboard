using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Abstractions;

/// <summary>
/// Identifies the node and tenant associated with an authenticated connector credential.
/// </summary>
/// <param name="NodeId">Dashboard-assigned node identifier.</param>
/// <param name="TenantId">Tenant that owns the enrolled node.</param>
public sealed record ConnectorNodeIdentity(
    Guid NodeId,
    string TenantId);

/// <summary>
/// Persists enrolled connectors and their latest manager observations.
/// </summary>
public interface IFleetStore
{
  /// <summary>
  /// Initializes the backing store and applies pending schema migrations.
  /// </summary>
  /// <param name="cancellationToken">Token that cancels initialization.</param>
  /// <returns>A task that completes after the store is ready.</returns>
  Task InitializeAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Enrolls or rotates the credential for one stable connector installation.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the connector.</param>
  /// <param name="connectorInstanceId">Stable connector installation identifier.</param>
  /// <param name="displayName">Operator-facing server name.</param>
  /// <param name="credentialHash">One-way hash of the newly issued node credential.</param>
  /// <param name="enrolledAt">Dashboard time of enrollment or rotation.</param>
  /// <param name="cancellationToken">Token that cancels enrollment.</param>
  /// <returns>The dashboard-assigned node identifier.</returns>
  Task<Guid> EnrollNodeAsync(
      string tenantId,
      string connectorInstanceId,
      string displayName,
      string credentialHash,
      DateTimeOffset enrolledAt,
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
  /// <param name="cancellationToken">Token that cancels synchronization.</param>
  /// <returns>A task that completes after the projection is committed.</returns>
  Task ApplySyncAsync(
      Guid nodeId,
      string connectorVersion,
      DateTimeOffset receivedAt,
      IReadOnlyList<ManagerObservedState> profiles,
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
}
