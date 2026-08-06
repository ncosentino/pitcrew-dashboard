using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Abstractions;

/// <summary>
/// Selects which visible incident lifecycle states a tenant query returns.
/// </summary>
public enum AlertIncidentFilter
{
  /// <summary>
  /// Returns triggered and acknowledged incidents that have not resolved.
  /// </summary>
  Active,

  /// <summary>
  /// Returns resolved incident history.
  /// </summary>
  Resolved,

  /// <summary>
  /// Returns active and resolved incidents.
  /// </summary>
  All,
}

/// <summary>
/// Identifies the outcome of acknowledging one incident.
/// </summary>
public enum AlertAcknowledgeStatus
{
  /// <summary>
  /// The active incident is acknowledged.
  /// </summary>
  Succeeded,

  /// <summary>
  /// The incident does not exist in the requested tenant.
  /// </summary>
  NotFound,

  /// <summary>
  /// The incident resolved before it could be acknowledged.
  /// </summary>
  Resolved,
}

/// <summary>
/// Carries the latest bounded alert evidence for every enrolled node.
/// </summary>
/// <param name="Nodes">Nodes and current profile evidence, including revoked nodes so their incidents can resolve.</param>
public sealed record AlertEvidenceSnapshot(
    IReadOnlyList<AlertNodeEvidence> Nodes);

/// <summary>
/// Carries current evidence for one enrolled node.
/// </summary>
/// <param name="TenantId">Tenant that owns the node.</param>
/// <param name="NodeId">Dashboard-assigned node identifier.</param>
/// <param name="DisplayName">Operator-facing node name.</param>
/// <param name="EnrolledAt">Dashboard time when the node was enrolled.</param>
/// <param name="LastSeenAt">Dashboard time of the latest accepted connector synchronization.</param>
/// <param name="IsRevoked">Whether the node credential is revoked.</param>
/// <param name="Profiles">Current profile evidence for the node.</param>
public sealed record AlertNodeEvidence(
    string TenantId,
    Guid NodeId,
    string DisplayName,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? LastSeenAt,
    bool IsRevoked,
    IReadOnlyList<AlertProfileEvidence> Profiles)
{
  public IReadOnlyList<AlertHostPressureSample> RecentHostPressureSamples { get; init; } = [];
}

/// <summary>
/// Carries one deduplicated Docker-host pressure sample for node-scoped alert evaluation.
/// </summary>
public sealed record AlertHostPressureSample(
    DateTimeOffset ObservedAt,
    string Status,
    int? LogicalProcessorCount,
    double? CpuUtilizationPercent,
    double? Load1,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes,
    double? CpuPressureSomeAvg10,
    double? MemoryPressureSomeAvg10,
    double? IoPressureSomeAvg10);

/// <summary>
/// Carries current and bounded recent evidence for one profile.
/// </summary>
/// <param name="Observation">Latest manager observation.</param>
/// <param name="Journal">Dashboard-retained journal continuity state.</param>
/// <param name="RecentResourceSamples">Newest bounded resource samples in ascending observation order.</param>
/// <param name="LatestCapacityCommand">Latest capacity command, when one exists.</param>
/// <param name="LatestRecoveryCommand">Latest recovery command, when one exists.</param>
public sealed record AlertProfileEvidence(
    ManagerObservedState Observation,
    AlertJournalEvidence Journal,
    IReadOnlyList<AlertResourceSample> RecentResourceSamples,
    AlertCommandEvidence? LatestCapacityCommand,
    AlertCommandEvidence? LatestRecoveryCommand);

/// <summary>
/// Carries the durable journal state needed to diagnose current history availability.
/// </summary>
/// <param name="Status">Latest manager journal status.</param>
/// <param name="ManagerDroppedEvents">Entries the manager reported discarding.</param>
/// <param name="MissedEvents">Durable sequences the dashboard knows were not delivered.</param>
/// <param name="UndeliveredEvents">Manager-retained sequences above the highest delivered sequence.</param>
/// <param name="EpochResets">Detected manager journal resets.</param>
/// <param name="RejectedFutureEvents">Events rejected because their timestamps exceeded allowed skew.</param>
/// <param name="HistoryExpiredAt">Dashboard time when this profile's retained history expired.</param>
public sealed record AlertJournalEvidence(
    string Status,
    int ManagerDroppedEvents,
    long MissedEvents,
    long UndeliveredEvents,
    long EpochResets,
    long RejectedFutureEvents,
    DateTimeOffset? HistoryExpiredAt);

/// <summary>
/// Carries one bounded resource sample used only for sustained-pressure evaluation.
/// </summary>
/// <param name="ObservedAt">Manager observation time.</param>
/// <param name="Status">Telemetry availability status.</param>
/// <param name="CpuCores">Combined manager and worker CPU cores when fully measured.</param>
/// <param name="HostLogicalProcessors">Host logical processor count when available.</param>
/// <param name="MemoryBytes">Combined manager and worker working-set bytes when fully measured.</param>
/// <param name="HostMemoryBytes">Host memory capacity when available.</param>
/// <param name="NetworkBytes">Combined cumulative worker network bytes when available.</param>
/// <param name="BlockIoBytes">Combined cumulative worker block-I/O bytes when available.</param>
public sealed record AlertResourceSample(
    DateTimeOffset ObservedAt,
    string Status,
    double? CpuCores,
    int? HostLogicalProcessors,
    long? MemoryBytes,
    long? HostMemoryBytes,
    long? NetworkBytes,
    long? BlockIoBytes);

/// <summary>
/// Carries the latest durable command outcome for one profile operation type.
/// </summary>
/// <param name="CommandId">Dashboard-assigned command identifier.</param>
/// <param name="Kind">Operation kind: capacity or recovery.</param>
/// <param name="Status">Current command status.</param>
/// <param name="CompletedAt">Terminal completion time, when available.</param>
/// <param name="FailureCategory">Bounded recovery failure category, when available.</param>
/// <param name="Message">Bounded operator-facing outcome detail.</param>
public sealed record AlertCommandEvidence(
    Guid CommandId,
    string Kind,
    string Status,
    DateTimeOffset? CompletedAt,
    string? FailureCategory,
    string? Message);

/// <summary>
/// Describes one currently proven condition to reconcile into durable incident state.
/// </summary>
/// <param name="Key">Stable identity for one condition or event episode.</param>
/// <param name="TenantId">Tenant that owns the evidence.</param>
/// <param name="NodeId">Node associated with the condition.</param>
/// <param name="ProfileId">Profile associated with the condition, or <see langword="null"/> for node-wide conditions.</param>
/// <param name="Kind">Closed alert kind used for filtering and display.</param>
/// <param name="Severity">Severity: warning or critical.</param>
/// <param name="FirstObservedAt">Earliest time the condition is proven to have been present.</param>
/// <param name="Debounce">Required continuous duration before the incident triggers.</param>
/// <param name="Title">Short operator-facing incident title.</param>
/// <param name="Summary">Current bounded incident summary.</param>
/// <param name="Reason">Machine-readable manager or dashboard reason.</param>
/// <param name="Evidence">Sanitized bounded evidence, or <see langword="null"/>.</param>
/// <param name="Link">Tenant-scoped UI path for the relevant node or profile.</param>
public sealed record AlertCandidate(
    string Key,
    string TenantId,
    Guid NodeId,
    string? ProfileId,
    string Kind,
    string Severity,
    DateTimeOffset FirstObservedAt,
    TimeSpan Debounce,
    string Title,
    string Summary,
    string Reason,
    string? Evidence,
    string Link);

/// <summary>
/// Prevents unavailable evidence from falsely resolving a previously triggered diagnosis.
/// </summary>
/// <remarks>
/// Pending incidents have their debounce restarted while evidence is unavailable, so an unknown gap
/// never counts toward a continuous debounce. Triggered and acknowledged incidents remain open until
/// fresh evidence either reproduces the candidate or proves the condition cleared.
/// </remarks>
/// <param name="Key">Exact candidate key to preserve, or <see langword="null"/> for a scoped suppression.</param>
/// <param name="NodeId">Node whose evidence is unavailable.</param>
/// <param name="ProfileId">Specific profile, or <see langword="null"/> for every profile on the node.</param>
/// <param name="Kind">Specific alert kind, or <see langword="null"/> for every profile-scoped kind.</param>
public sealed record AlertSuppression(
    string? Key,
    Guid NodeId,
    string? ProfileId,
    string? Kind);

/// <summary>
/// Returns all currently proven candidates plus diagnoses whose evidence is temporarily unavailable.
/// </summary>
/// <param name="Candidates">Complete set of currently proven alert conditions.</param>
/// <param name="Suppressions">Diagnoses that cannot currently be proven clear or unhealthy.</param>
public sealed record AlertEvaluationResult(
    IReadOnlyList<AlertCandidate> Candidates,
    IReadOnlyList<AlertSuppression> Suppressions);

/// <summary>
/// Describes one durable operational incident.
/// </summary>
/// <param name="IncidentId">Dashboard-assigned incident identifier.</param>
/// <param name="NodeId">Node associated with the incident.</param>
/// <param name="ProfileId">Profile associated with the incident, or <see langword="null"/>.</param>
/// <param name="Kind">Closed alert kind.</param>
/// <param name="Severity">Severity: warning or critical.</param>
/// <param name="Status">Lifecycle status: triggered, acknowledged, or resolved.</param>
/// <param name="Title">Short operator-facing incident title.</param>
/// <param name="Summary">Latest bounded incident summary.</param>
/// <param name="Reason">Machine-readable manager or dashboard reason.</param>
/// <param name="Evidence">Sanitized bounded evidence, or <see langword="null"/>.</param>
/// <param name="Link">Tenant-scoped UI path for the relevant node or profile.</param>
/// <param name="FirstObservedAt">Earliest time the condition was proven present.</param>
/// <param name="TriggeredAt">Time the debounce boundary was reached.</param>
/// <param name="LastObservedAt">Latest evaluation that still observed the condition.</param>
/// <param name="AcknowledgedAt">Time an administrator acknowledged the incident.</param>
/// <param name="AcknowledgedByGitHubUserId">Acknowledging GitHub user identifier.</param>
/// <param name="ResolvedAt">Time the condition cleared.</param>
public sealed record AlertIncident(
    Guid IncidentId,
    Guid NodeId,
    string? ProfileId,
    string Kind,
    string Severity,
    string Status,
    string Title,
    string Summary,
    string Reason,
    string? Evidence,
    string Link,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset TriggeredAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedByGitHubUserId,
    DateTimeOffset? ResolvedAt);

/// <summary>
/// Returns bounded visible incidents for one tenant.
/// </summary>
/// <param name="GeneratedAt">Dashboard time when the response was generated.</param>
/// <param name="Incidents">Visible incidents ordered newest first.</param>
/// <param name="Truncated">Whether the query limit hid older matching incidents.</param>
public sealed record AlertIncidentPage(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AlertIncident> Incidents,
    bool Truncated);

/// <summary>
/// Loads bounded current and recent evidence for alert evaluation.
/// </summary>
public interface IAlertEvidenceStore
{
  /// <summary>
  /// Loads every enrolled node plus a bounded number of recent samples for each current profile.
  /// </summary>
  /// <param name="resourceWindowStart">Oldest resource observation needed by the evaluator.</param>
  /// <param name="maximumSamplesPerProfile">Maximum newest samples returned for one profile.</param>
  /// <param name="cancellationToken">Token that cancels evidence loading.</param>
  /// <returns>The bounded alert evidence snapshot.</returns>
  Task<AlertEvidenceSnapshot> LoadAsync(
      DateTimeOffset resourceWindowStart,
      int maximumSamplesPerProfile,
      CancellationToken cancellationToken);
}

/// <summary>
/// Persists restart-safe incident lifecycle state.
/// </summary>
public interface IAlertIncidentStore
{
  /// <summary>
  /// Reconciles all currently proven candidates with pending and active incident state.
  /// </summary>
  /// <param name="candidates">Complete set of currently proven conditions.</param>
  /// <param name="suppressions">Existing diagnoses that unavailable evidence cannot currently resolve.</param>
  /// <param name="evaluatedAt">Dashboard time of evaluation.</param>
  /// <param name="resolvedBefore">Resolved incidents older than this time may be deleted.</param>
  /// <param name="maximumResolvedPerTenant">Hard retained resolved-history ceiling per tenant.</param>
  /// <param name="cancellationToken">Token that cancels reconciliation.</param>
  /// <returns>A task that completes after the atomic reconciliation.</returns>
  Task ReconcileAsync(
      IReadOnlyList<AlertCandidate> candidates,
      IReadOnlyList<AlertSuppression> suppressions,
      DateTimeOffset evaluatedAt,
      DateTimeOffset resolvedBefore,
      int maximumResolvedPerTenant,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads bounded visible incidents for one tenant.
  /// </summary>
  /// <param name="tenantId">Tenant whose incidents should be returned.</param>
  /// <param name="filter">Visible lifecycle states to include.</param>
  /// <param name="limit">Maximum incidents returned.</param>
  /// <param name="generatedAt">Dashboard time when the response is generated.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The bounded incident page.</returns>
  Task<AlertIncidentPage> GetAsync(
      string tenantId,
      AlertIncidentFilter filter,
      int limit,
      DateTimeOffset generatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Acknowledges one active incident without resolving or deleting it.
  /// </summary>
  /// <param name="tenantId">Tenant that owns the incident.</param>
  /// <param name="incidentId">Incident to acknowledge.</param>
  /// <param name="acknowledgedByGitHubUserId">Administrator acknowledging the incident.</param>
  /// <param name="acknowledgedAt">Dashboard time of acknowledgement.</param>
  /// <param name="cancellationToken">Token that cancels acknowledgement.</param>
  /// <returns>The acknowledgement result.</returns>
  Task<AlertAcknowledgeStatus> AcknowledgeAsync(
      string tenantId,
      Guid incidentId,
      string acknowledgedByGitHubUserId,
      DateTimeOffset acknowledgedAt,
      CancellationToken cancellationToken);
}
