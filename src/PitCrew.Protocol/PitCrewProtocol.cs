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
  public const int Version = 10;

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
/// <param name="Activity">Demand-driven runner activity when reported; otherwise <see langword="null"/>.</param>
/// <param name="Target">Current scale-set target associated with the slot when reported; otherwise <see langword="null"/>.</param>
/// <param name="RegistrationStatus">GitHub registration eligibility when reported; otherwise <see langword="null"/>.</param>
/// <param name="ImageId">Immutable local Docker image identity (<c>sha256:</c> and 64 hexadecimal characters) when reported; otherwise <see langword="null"/>.</param>
/// <param name="LastExit">Bounded manager contract 11 exit evidence when available; otherwise <see langword="null"/>, which never means a clean exit.</param>
/// <param name="RunnerNameHash">Manager contract 14 lowercase SHA-256 correlation key for the exact runner name when available; otherwise <see langword="null"/>.</param>
/// <param name="CurrentJob">Manager contract 15 bounded active job context when available; otherwise <see langword="null"/>.</param>
public sealed record ObservedSlotState(
    string Key,
    string? Repository,
    bool Desired,
    bool ProcessRunning,
    string State,
    int FailureCount,
    int BackoffSeconds,
    DateTimeOffset? UpdatedAt,
    ResourceUsage? Resources,
    string? Activity,
    string? Target,
    string? RegistrationStatus = null,
    string? ImageId = null,
    WorkerLastExitDiagnostic? LastExit = null,
    string? RunnerNameHash = null,
    CurrentJobContext? CurrentJob = null);

/// <summary>
/// Describes the demand-driven scale-set projection published by one profile manager.
/// </summary>
/// <param name="Mode">Autoscaling mode implemented by the manager.</param>
/// <param name="Status">Current autoscaling lifecycle status.</param>
/// <param name="MinimumIdleSlots">Minimum number of idle runners the manager attempts to retain.</param>
/// <param name="MaximumSlots">Configured upper bound for aggregate scale-set capacity.</param>
/// <param name="TargetSlots">Current aggregate slot activation target.</param>
/// <param name="AssignedJobs">Jobs currently assigned to the profile's scale sets.</param>
/// <param name="RunningJobs">Assigned jobs that are currently running.</param>
/// <param name="AvailableJobs">Assigned jobs that remain available for a runner.</param>
/// <param name="IdleRunners">Live runners currently waiting for work.</param>
/// <param name="BusyRunners">Live runners currently executing work.</param>
/// <param name="ScaleDownDelaySeconds">Configured delay before surplus capacity is removed.</param>
/// <param name="ScaleSetCount">Number of scale sets contributing to the aggregate projection.</param>
/// <param name="ScaleDownAt">Scheduled scale-down time when surplus capacity is pending; otherwise <see langword="null"/>.</param>
/// <param name="LastError">Most recent autoscaling error when degraded; otherwise <see langword="null"/>.</param>
/// <param name="MaximumActiveWorkers">Manager contract 11 profile-wide active-worker admission ceiling when reported; otherwise <see langword="null"/>.</param>
/// <param name="Targets">Manager contract 11 per-target local and GitHub evidence when reported; otherwise <see langword="null"/>.</param>
public sealed record ManagerAutoscalingState(
    [property: JsonRequired] string Mode,
    [property: JsonRequired] string Status,
    [property: JsonRequired] int MinimumIdleSlots,
    [property: JsonRequired] int MaximumSlots,
    [property: JsonRequired] int TargetSlots,
    [property: JsonRequired] int AssignedJobs,
    [property: JsonRequired] int RunningJobs,
    [property: JsonRequired] int AvailableJobs,
    [property: JsonRequired] int IdleRunners,
    [property: JsonRequired] int BusyRunners,
    [property: JsonRequired] int ScaleDownDelaySeconds,
    [property: JsonRequired] int ScaleSetCount,
    [property: JsonRequired] DateTimeOffset? ScaleDownAt,
    [property: JsonRequired] string? LastError,
    int? MaximumActiveWorkers = null,
    IReadOnlyList<AutoscalingTargetState>? Targets = null);

/// <summary>
/// Describes convergence from existing workers to one configured worker-image revision.
/// </summary>
/// <param name="Status">Current convergence status: current, rolling, or degraded.</param>
/// <param name="TargetImage">Configured OCI image reference when reported; otherwise <see langword="null"/>.</param>
/// <param name="TargetImageId">Resolved immutable local image identity when reported; otherwise <see langword="null"/>.</param>
/// <param name="TargetRevision">SHA-256 worker revision derived from the complete worker contract.</param>
/// <param name="CurrentWorkers">Live workers already using the target revision.</param>
/// <param name="StaleWorkers">Live workers retained on an older revision until they can exit safely.</param>
/// <param name="LastError">Most recent rollout error when degraded; otherwise <see langword="null"/>.</param>
public sealed record ManagerWorkerUpdateState(
    [property: JsonRequired] string Status,
    string? TargetImage,
    string? TargetImageId,
    [property: JsonRequired] string TargetRevision,
    [property: JsonRequired] int CurrentWorkers,
    [property: JsonRequired] int StaleWorkers,
    [property: JsonRequired] string? LastError);

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
/// <param name="ConfiguredSlots">Configured maximum slot count when reported; otherwise <see langword="null"/>.</param>
/// <param name="Autoscaling">Demand-driven autoscaling projection, or <see langword="null"/> for fixed-capacity profiles.</param>
/// <param name="EligibleSlots">Number of slots GitHub currently reports as connected, or <see langword="null"/> when unavailable.</param>
/// <param name="ResourcePolicy">Manager contract 11 per-worker resource admission policy when reported; otherwise <see langword="null"/>.</param>
/// <param name="OperationJournal">Manager contract 12 bounded durable operation journal when reported; otherwise <see langword="null"/>.</param>
/// <param name="SubsystemHealth">Manager contract 12 Docker and GitHub operation health when reported; otherwise <see langword="null"/>.</param>
/// <param name="CapacityEvidence">Manager contract 12 fixed or per-target capacity-deficit evidence when reported; otherwise <see langword="null"/>.</param>
/// <param name="Update">Worker-image convergence evidence when reported; otherwise <see langword="null"/>.</param>
/// <param name="Host">Manager contract 13 sanitized node hardware inventory when reported.</param>
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
    ManagerResourceTelemetry? ResourceTelemetry,
    int? ConfiguredSlots,
    ManagerAutoscalingState? Autoscaling,
    int? EligibleSlots = null,
    WorkerResourcePolicy? ResourcePolicy = null,
    ManagerOperationJournal? OperationJournal = null,
    ManagerSubsystemHealth? SubsystemHealth = null,
    ManagerCapacityEvidence? CapacityEvidence = null,
    ManagerWorkerUpdateState? Update = null,
    ObservedHost? Host = null);

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
/// Describes one profile whose single capacity target may be changed remotely.
/// </summary>
/// <param name="ProfileId">Locally resolved profile identifier.</param>
/// <param name="Generation">Current desired-capacity generation.</param>
/// <param name="CurrentMaximum">Current configured maximum.</param>
/// <param name="MaximumAllowed">Local policy ceiling enforced by the connector.</param>
/// <param name="SupportsZeroMaximum">Whether the local PitCrew profile supports explicit zero-capacity pause.</param>
public sealed record CapacityOperatorProfile(
    [property: JsonRequired] string ProfileId,
    [property: JsonRequired] int Generation,
    [property: JsonRequired] int CurrentMaximum,
    [property: JsonRequired] int MaximumAllowed,
    bool SupportsZeroMaximum = false);

/// <summary>
/// Advertises the locally enabled capacity-operation surface.
/// </summary>
/// <param name="Profiles">Profiles whose single existing capacity target is controllable.</param>
public sealed record CapacityOperatorCapability(
    [property: JsonRequired]
    IReadOnlyList<CapacityOperatorProfile> Profiles);

/// <summary>
/// Requests one absolute capacity maximum through an outbound connector.
/// </summary>
/// <param name="CommandId">Dashboard-assigned idempotency identifier.</param>
/// <param name="ProfileId">Locally resolved profile identifier.</param>
/// <param name="ExpectedGeneration">Generation that must still be current before execution.</param>
/// <param name="Maximum">Requested absolute capacity maximum.</param>
/// <param name="ExpiresAt">Time after which the connector must reject the command.</param>
public sealed record SetCapacityCommand(
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string ProfileId,
    [property: JsonRequired] int ExpectedGeneration,
    [property: JsonRequired] int Maximum,
    [property: JsonRequired] DateTimeOffset ExpiresAt);

/// <summary>
/// Reports the locally observed result of one capacity command.
/// </summary>
/// <param name="CommandId">Command identifier supplied by the dashboard.</param>
/// <param name="Status">Final status: succeeded, rejected, or failed.</param>
/// <param name="Message">Bounded operator-facing result detail.</param>
/// <param name="AcceptedGeneration">Acknowledged generation after success; otherwise <see langword="null"/>.</param>
/// <param name="CompletedAt">Connector time when execution completed.</param>
public sealed record CapacityCommandOutcome(
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string Status,
    [property: JsonRequired] string? Message,
    [property: JsonRequired] int? AcceptedGeneration,
    [property: JsonRequired] DateTimeOffset CompletedAt);

/// <summary>
/// Reports the connector's bounded current and most recently recovered health state.
/// </summary>
public sealed record ConnectorHealthReplaySnapshot(
    [property: JsonRequired] string State,
    [property: JsonRequired] DateTimeOffset ProcessStartedAt,
    [property: JsonRequired] DateTimeOffset UpdatedAt,
    [property: JsonRequired] DateTimeOffset? LastAttemptAt,
    [property: JsonRequired] DateTimeOffset? LastSuccessAt,
    [property: JsonRequired] Guid? ActiveOutageId,
    [property: JsonRequired] DateTimeOffset? ActiveOutageStartedAt,
    [property: JsonRequired] DateTimeOffset? LastFailureAt,
    [property: JsonRequired] string? LastFailureCategory,
    [property: JsonRequired] string? LastFailureProfileId,
    [property: JsonRequired] string? LastFailureDetail,
    [property: JsonRequired] int ConsecutiveFailures,
    [property: JsonRequired] DateTimeOffset? NextRetryAt,
    [property: JsonRequired] Guid? LastRecoveredOutageId,
    [property: JsonRequired] DateTimeOffset? LastRecoveredOutageStartedAt,
    [property: JsonRequired] DateTimeOffset? LastRecoveredAt,
    [property: JsonRequired] string? LastRecoveredFailureCategory);

/// <summary>
/// Reports one sanitized connector-health journal event.
/// </summary>
public sealed record ConnectorHealthReplayEvent(
    [property: JsonRequired] Guid EventId,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] DateTimeOffset OccurredAt,
    [property: JsonRequired] string State,
    [property: JsonRequired] Guid? OutageId,
    [property: JsonRequired] DateTimeOffset? OutageStartedAt,
    [property: JsonRequired] string? FailureCategory,
    [property: JsonRequired] string? ProfileId,
    [property: JsonRequired] int ConsecutiveFailures,
    [property: JsonRequired] int? RetryDelaySeconds,
    [property: JsonRequired] string? Detail);

/// <summary>
/// Carries bounded connector health evidence after the normal synchronization path recovers.
/// </summary>
public sealed record ConnectorHealthReplay(
    [property: JsonRequired] ConnectorHealthReplaySnapshot Snapshot,
    [property: JsonRequired] IReadOnlyList<ConnectorHealthReplayEvent> Events);

/// <summary>
/// Acknowledges connector-health events durably accepted by Dashboard.
/// </summary>
public sealed record ConnectorHealthAcknowledgement(
    [property: JsonRequired] IReadOnlyList<Guid> EventIds);

/// <summary>
/// Validates the bounded connector-health replay contract shared by connector and Dashboard.
/// </summary>
public static class ConnectorHealthReplayContract
{
  /// <summary>
  /// Gets the maximum event count accepted in one synchronization request.
  /// </summary>
  public const int MaximumEvents = 256;

  /// <summary>
  /// Validates one optional replay envelope without throwing.
  /// </summary>
  public static bool IsValid(
      ConnectorHealthReplay? replay,
      DateTimeOffset maximumTimestamp)
  {
    if (replay is null)
    {
      return true;
    }
    if (replay.Snapshot is null ||
        !IsValidSnapshot(
            replay.Snapshot,
            maximumTimestamp) ||
        replay.Events is null ||
        replay.Events.Count > MaximumEvents)
    {
      return false;
    }
    var eventIds = new HashSet<Guid>();
    return replay.Events.All(entry =>
        entry is not null &&
        eventIds.Add(entry.EventId) &&
        IsValidEvent(
            entry,
            maximumTimestamp));
  }

  private static bool IsValidSnapshot(
      ConnectorHealthReplaySnapshot snapshot,
      DateTimeOffset maximumTimestamp)
  {
    if (!IsState(snapshot.State) ||
        snapshot.ProcessStartedAt == default ||
        snapshot.UpdatedAt == default ||
        snapshot.ProcessStartedAt > snapshot.UpdatedAt ||
        snapshot.UpdatedAt > maximumTimestamp ||
        snapshot.ConsecutiveFailures is < 0 or > 1_000_000 ||
        snapshot.ActiveOutageId == Guid.Empty ||
        snapshot.LastRecoveredOutageId == Guid.Empty ||
        !IsOptionalProfileId(snapshot.LastFailureProfileId) ||
        !IsFailure(
            snapshot.LastFailureCategory,
            snapshot.LastFailureDetail) ||
        !IsFailureCategory(
            snapshot.LastRecoveredFailureCategory))
    {
      return false;
    }
    if ((snapshot.ActiveOutageId is null) !=
        (snapshot.ActiveOutageStartedAt is null) ||
        snapshot.ActiveOutageStartedAt > snapshot.UpdatedAt ||
        snapshot.LastFailureAt > snapshot.UpdatedAt ||
        snapshot.LastAttemptAt > snapshot.UpdatedAt ||
        snapshot.LastSuccessAt > snapshot.UpdatedAt ||
        snapshot.NextRetryAt < snapshot.LastFailureAt ||
        snapshot.NextRetryAt > snapshot.UpdatedAt.AddDays(1))
    {
      return false;
    }
    if (snapshot.ActiveOutageId is not null &&
        (snapshot.State != "degraded" ||
         snapshot.LastFailureAt is null ||
         snapshot.LastFailureCategory is null))
    {
      return false;
    }
    if (snapshot.State == "healthy" &&
        (snapshot.ConsecutiveFailures != 0 ||
         snapshot.NextRetryAt is not null))
    {
      return false;
    }
    if ((snapshot.LastRecoveredOutageId is null) !=
        (snapshot.LastRecoveredOutageStartedAt is null) ||
        (snapshot.LastRecoveredOutageId is null) !=
        (snapshot.LastRecoveredAt is null))
    {
      return false;
    }
    return snapshot.LastRecoveredOutageId is null ||
        (snapshot.LastRecoveredOutageStartedAt <=
             snapshot.LastRecoveredAt &&
         snapshot.LastRecoveredAt <= snapshot.UpdatedAt);
  }

  private static bool IsValidEvent(
      ConnectorHealthReplayEvent entry,
      DateTimeOffset maximumTimestamp)
  {
    if (entry.EventId == Guid.Empty ||
        entry.OccurredAt == default ||
        entry.OccurredAt > maximumTimestamp ||
        !IsEventKind(entry.Kind) ||
        !IsState(entry.State) ||
        !IsOptionalProfileId(entry.ProfileId) ||
        !IsFailure(
            entry.FailureCategory,
            entry.Detail) ||
        entry.ConsecutiveFailures is < 0 or > 1_000_000 ||
        entry.RetryDelaySeconds is < 0 or > 86_400 ||
        entry.OutageId == Guid.Empty ||
        (entry.OutageId is null) !=
            (entry.OutageStartedAt is null) ||
        entry.OutageStartedAt > entry.OccurredAt)
    {
      return false;
    }
    return entry.Kind is not (
        "synchronization-failed" or
        "observation-incomplete" or
        "enrollment-failed" or
        "rejected")
        || entry.FailureCategory is not null;
  }

  private static bool IsState(string state) =>
      state is "starting" or "healthy" or "degraded" or "stopping";

  private static bool IsEventKind(string kind) =>
      kind is
          "process-started" or
          "process-stopping" or
          "synchronization-succeeded" or
          "synchronization-failed" or
          "observation-incomplete" or
          "enrollment-failed" or
          "rejected" or
          "recovered";

  private static bool IsOptionalProfileId(string? profileId) =>
      profileId is null || PitCrewProfileId.IsValid(profileId);

  private static bool IsFailure(
      string? category,
      string? detail)
  {
    if (category is null)
    {
      return detail is null;
    }
    if (!IsFailureCategory(category))
    {
      return false;
    }
    var expectedDetail = category switch
    {
      "state-root-missing" =>
          "PitCrew state root is unavailable.",
      "state-root-unreadable" =>
          "PitCrew state root could not be enumerated.",
      "profile-directory-unreadable" =>
          "Profile state directory could not be inspected.",
      "profile-state-invalid" =>
          "Profile observed state is invalid.",
      "profile-state-unreadable" =>
          "Profile observed state could not be read.",
      "synchronization-network" =>
          "Connector synchronization could not reach Dashboard.",
      "synchronization-timeout" =>
          "Dashboard synchronization timed out.",
      "synchronization-rate-limited" =>
          "Dashboard rate-limited connector synchronization.",
      "synchronization-server" =>
          "Dashboard returned a transient server error during synchronization.",
      "synchronization-io" =>
          "Connector synchronization could not read or write local state.",
      "payload-rejected" =>
          "Dashboard permanently rejected the synchronization payload.",
      "credential-rejected" =>
          "Dashboard rejected the connector credential.",
      "enrollment-rejected" =>
          "Dashboard rejected connector enrollment.",
      "enrollment-network" =>
          "Connector enrollment could not reach Dashboard.",
      "enrollment-timeout" =>
          "Connector enrollment timed out.",
      "enrollment-rate-limited" =>
          "Dashboard rate-limited connector enrollment.",
      "enrollment-server" =>
          "Dashboard returned a transient server error during enrollment.",
      "configuration-invalid" =>
          "Connector configuration is invalid.",
      "enrollment-configuration" =>
          "Connector enrollment configuration is incomplete.",
      _ => null,
    };
    return detail is null ||
        string.Equals(
            detail,
            expectedDetail,
            StringComparison.Ordinal);
  }

  private static bool IsFailureCategory(
      string? category) =>
      category is null or
          "state-root-missing" or
          "state-root-unreadable" or
          "profile-directory-unreadable" or
          "profile-state-invalid" or
          "profile-state-unreadable" or
          "synchronization-network" or
          "synchronization-timeout" or
          "synchronization-rate-limited" or
          "synchronization-server" or
          "synchronization-io" or
          "payload-rejected" or
          "credential-rejected" or
          "enrollment-rejected" or
          "enrollment-network" or
          "enrollment-timeout" or
          "enrollment-rate-limited" or
          "enrollment-server" or
          "configuration-invalid" or
          "enrollment-configuration";
}

/// <summary>
/// Sends the latest complete profile projections from one authenticated connector.
/// </summary>
/// <param name="ProtocolVersion">Connector synchronization protocol version.</param>
/// <param name="ConnectorVersion">Connector application version.</param>
/// <param name="SentAt">Connector time when the request was created.</param>
/// <param name="Profiles">Latest readable profile projections from configured state roots.</param>
/// <param name="CapacityOperator">Locally enabled capacity-operation capability, or <see langword="null"/>.</param>
/// <param name="CapacityCommandOutcome">Most recent unacknowledged command outcome, or <see langword="null"/>.</param>
/// <param name="RecoveryOperator">Locally enabled manager-recovery capability, or <see langword="null"/>.</param>
/// <param name="RecoveryCommandProgress">Most recent unacknowledged recovery progress report, or <see langword="null"/>.</param>
/// <param name="RecoveryCommandOutcome">Most recent unacknowledged recovery outcome, or <see langword="null"/>.</param>
/// <param name="ConnectorHealth">Bounded local connector-health evidence, or <see langword="null"/> when unavailable.</param>
public sealed record ConnectorSyncRequest(
    int ProtocolVersion,
    string ConnectorVersion,
    DateTimeOffset SentAt,
    IReadOnlyList<ManagerObservedState> Profiles,
    CapacityOperatorCapability? CapacityOperator,
    CapacityCommandOutcome? CapacityCommandOutcome,
    RecoveryOperatorCapability? RecoveryOperator = null,
    RecoveryCommandProgress? RecoveryCommandProgress = null,
    RecoveryCommandOutcome? RecoveryCommandOutcome = null,
    ConnectorHealthReplay? ConnectorHealth = null);

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
/// <param name="CapacityCommand">Capacity command claimed for this connector, or <see langword="null"/>.</param>
/// <param name="RecoveryCommand">Manager-recovery command claimed for this connector, or <see langword="null"/>.</param>
/// <param name="ConnectorHealthAcknowledgement">Connector-health event identifiers durably accepted by Dashboard.</param>
public sealed record ConnectorSyncResponse(
    DateTimeOffset AcceptedAt,
    int NextPollSeconds,
    ConnectorCredentialRotation? CredentialRotation,
    SetCapacityCommand? CapacityCommand,
    RecoverManagerCommand? RecoveryCommand = null,
    ConnectorHealthAcknowledgement? ConnectorHealthAcknowledgement = null);

/// <summary>
/// Provides source-generated JSON metadata for connector and dashboard protocol messages.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ResourceUsage))]
[JsonSerializable(typeof(WorkerResourcePolicy))]
[JsonSerializable(typeof(WorkerLastExitDiagnostic))]
[JsonSerializable(typeof(ScaleSetStatistics))]
[JsonSerializable(typeof(AutoscalingTargetState))]
[JsonSerializable(typeof(HostResourceCapacity))]
[JsonSerializable(typeof(HostPressureTelemetry))]
[JsonSerializable(typeof(ManagerResourceTelemetry))]
[JsonSerializable(typeof(ManagerEvent))]
[JsonSerializable(typeof(ManagerOperationJournal))]
[JsonSerializable(typeof(SubsystemOperationEvidence))]
[JsonSerializable(typeof(SubsystemHealthSummary))]
[JsonSerializable(typeof(ManagerSubsystemHealth))]
[JsonSerializable(typeof(CapacityDeficitEvidence))]
[JsonSerializable(typeof(TargetCapacityDeficitEvidence))]
[JsonSerializable(typeof(ManagerCapacityEvidence))]
[JsonSerializable(typeof(ObservedSlotState))]
[JsonSerializable(typeof(CurrentJobContext))]
[JsonSerializable(typeof(ManagerAutoscalingState))]
[JsonSerializable(typeof(ManagerObservedState))]
[JsonSerializable(typeof(ConnectorEnrollmentRequest))]
[JsonSerializable(typeof(ConnectorEnrollmentResponse))]
[JsonSerializable(typeof(CapacityOperatorProfile))]
[JsonSerializable(typeof(CapacityOperatorCapability))]
[JsonSerializable(typeof(SetCapacityCommand))]
[JsonSerializable(typeof(CapacityCommandOutcome))]
[JsonSerializable(typeof(RecoveryOperatorProfile))]
[JsonSerializable(typeof(RecoveryOperatorCapability))]
[JsonSerializable(typeof(RecoverManagerCommand))]
[JsonSerializable(typeof(RecoveryCommandProgress))]
[JsonSerializable(typeof(RecoveryCommandOutcome))]
[JsonSerializable(typeof(ConnectorHealthReplaySnapshot))]
[JsonSerializable(typeof(ConnectorHealthReplayEvent))]
[JsonSerializable(typeof(ConnectorHealthReplay))]
[JsonSerializable(typeof(ConnectorHealthAcknowledgement))]
[JsonSerializable(typeof(IReadOnlyList<ConnectorHealthReplayEvent>))]
[JsonSerializable(typeof(ConnectorSyncRequest))]
[JsonSerializable(typeof(ConnectorCredentialRotation))]
[JsonSerializable(typeof(ConnectorSyncResponse))]
[JsonSerializable(typeof(IReadOnlyList<ManagerObservedState>))]
public sealed partial class PitCrewProtocolJsonContext : JsonSerializerContext;
