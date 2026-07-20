using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Defines the connector-to-dashboard protocol version implemented by this assembly.
/// </summary>
public static class PitCrewProtocol
{
  /// <summary>
  /// Gets the current connector synchronization protocol version.
  /// </summary>
  public const int Version = 2;

  /// <summary>
  /// Gets the oldest connector synchronization protocol accepted by the dashboard.
  /// </summary>
  public const int MinimumSupportedVersion = 1;
}

/// <summary>
/// Describes one manager-owned runner slot without exposing registration credentials or Docker access.
/// </summary>
/// <param name="Key">Stable profile-scoped slot key.</param>
/// <param name="Repository">Sanitized repository identity, or <see langword="null"/> for shared scopes.</param>
/// <param name="Desired">Whether the slot remains part of desired capacity.</param>
/// <param name="ProcessRunning">Whether the manager still owns a live slot process.</param>
/// <param name="State">Current lifecycle state reported by the manager.</param>
/// <param name="FailureCount">Consecutive registration or startup failures.</param>
/// <param name="BackoffSeconds">Backoff selected after the most recent failure or runner exit.</param>
/// <param name="UpdatedAt">Time the slot lifecycle state last changed.</param>
/// <param name="Resources">Point-in-time worker resource usage when available; otherwise <see langword="null"/>.</param>
public sealed record ObservedSlotState(
    string Key,
    string? Repository,
    bool Desired,
    bool ProcessRunning,
    string State,
    int FailureCount,
    int BackoffSeconds,
    DateTimeOffset? UpdatedAt,
    ResourceUsage? Resources);

/// <summary>
/// Represents the credential-free operational projection published by one Pitcrew profile manager.
/// </summary>
/// <param name="SchemaVersion">Observed-state document schema version.</param>
/// <param name="ManagerContractVersion">Runtime compatibility contract implemented by the manager.</param>
/// <param name="ProfileId">Profile identifier local to the connected server.</param>
/// <param name="ManagerInstanceId">Identifier regenerated whenever the manager process starts.</param>
/// <param name="ManagerStatus">Manager lifecycle status.</param>
/// <param name="ObservedAt">Time the manager published this projection.</param>
/// <param name="Scope">GitHub runner scope: repository, organization, or enterprise.</param>
/// <param name="Generation">Accepted desired-capacity generation.</param>
/// <param name="DesiredStateHash">Hash of the accepted desired-capacity document.</param>
/// <param name="DesiredStateStatus">Validation status of the latest desired-capacity document.</param>
/// <param name="DesiredSlots">Number of slots requested by accepted desired capacity.</param>
/// <param name="ActiveSlots">Number of slots whose manager process is still running.</param>
/// <param name="DrainingSlots">Number of active slots removed from desired capacity.</param>
/// <param name="Slots">Current slot projections.</param>
/// <param name="ResourceTelemetry">Point-in-time manager and host telemetry when available; otherwise <see langword="null"/>.</param>
public sealed record ManagerObservedState(
    int SchemaVersion,
    int ManagerContractVersion,
    string ProfileId,
    string ManagerInstanceId,
    string ManagerStatus,
    DateTimeOffset ObservedAt,
    string Scope,
    int Generation,
    string? DesiredStateHash,
    string DesiredStateStatus,
    int DesiredSlots,
    int ActiveSlots,
    int DrainingSlots,
    IReadOnlyList<ObservedSlotState> Slots,
    ManagerResourceTelemetry? ResourceTelemetry);

/// <summary>
/// Requests enrollment of one connector installation with a dashboard deployment.
/// </summary>
/// <param name="ConnectorInstanceId">Stable identifier generated and retained by the connector installation.</param>
/// <param name="DisplayName">Operator-facing server name.</param>
public sealed record ConnectorEnrollmentRequest(
    string ConnectorInstanceId,
    string DisplayName);

/// <summary>
/// Returns the node identity and node-scoped credential issued during enrollment.
/// </summary>
/// <param name="NodeId">Dashboard-assigned node identifier.</param>
/// <param name="Credential">High-entropy bearer credential shown only to the connector.</param>
public sealed record ConnectorEnrollmentResponse(
    Guid NodeId,
    string Credential);

/// <summary>
/// Sends the latest complete profile projections from one authenticated connector.
/// </summary>
/// <param name="ProtocolVersion">Connector synchronization protocol version.</param>
/// <param name="ConnectorVersion">Connector application version.</param>
/// <param name="SentAt">Connector time when the request was created.</param>
/// <param name="Profiles">Latest readable profile projections from configured state roots.</param>
public sealed record ConnectorSyncRequest(
    int ProtocolVersion,
    string ConnectorVersion,
    DateTimeOffset SentAt,
    IReadOnlyList<ManagerObservedState> Profiles);

/// <summary>
/// Delivers a staged replacement node credential to the connector.
/// </summary>
/// <param name="Credential">High-entropy replacement credential persisted before the next synchronization.</param>
public sealed record ConnectorCredentialRotation(string Credential);

/// <summary>
/// Acknowledges one connector synchronization request.
/// </summary>
/// <param name="AcceptedAt">Dashboard time when the synchronization was committed.</param>
/// <param name="NextPollSeconds">Recommended minimum delay before the next synchronization.</param>
/// <param name="CredentialRotation">Replacement credential when rotation was staged; otherwise <see langword="null"/>.</param>
public sealed record ConnectorSyncResponse(
    DateTimeOffset AcceptedAt,
    int NextPollSeconds,
    ConnectorCredentialRotation? CredentialRotation);

/// <summary>
/// Provides source-generated JSON metadata for connector and dashboard protocol messages.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ResourceUsage))]
[JsonSerializable(typeof(HostResourceCapacity))]
[JsonSerializable(typeof(ManagerResourceTelemetry))]
[JsonSerializable(typeof(ObservedSlotState))]
[JsonSerializable(typeof(ManagerObservedState))]
[JsonSerializable(typeof(ConnectorEnrollmentRequest))]
[JsonSerializable(typeof(ConnectorEnrollmentResponse))]
[JsonSerializable(typeof(ConnectorSyncRequest))]
[JsonSerializable(typeof(ConnectorCredentialRotation))]
[JsonSerializable(typeof(ConnectorSyncResponse))]
[JsonSerializable(typeof(IReadOnlyList<ManagerObservedState>))]
public sealed partial class PitCrewProtocolJsonContext : JsonSerializerContext;
