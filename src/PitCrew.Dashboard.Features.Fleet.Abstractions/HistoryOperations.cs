using PitCrew.Protocol;

namespace PitCrew.Dashboard.Features.Fleet.Abstractions;

/// <summary>
/// Selects the stored resolution returned by one bounded history query.
/// </summary>
public enum HistoryResolution
{
  /// <summary>
  /// Returns retained per-observation samples.
  /// </summary>
  Raw,

  /// <summary>
  /// Returns deterministic hourly rollups derived from retained samples.
  /// </summary>
  Hourly,
}

/// <summary>
/// Bounds one tenant-scoped history query in time and in returned points.
/// </summary>
/// <param name="From">Inclusive start of the requested range.</param>
/// <param name="To">Exclusive end of the requested range.</param>
/// <param name="Resolution">Stored resolution to return.</param>
/// <param name="PointLimit">Maximum samples or rollups returned per profile.</param>
/// <param name="EventLimit">Maximum manager events returned per profile.</param>
public sealed record HistoryWindow(
    DateTimeOffset From,
    DateTimeOffset To,
    HistoryResolution Resolution,
    int PointLimit,
    int EventLimit);

/// <summary>
/// Bounds retained history by measured policy.
/// </summary>
/// <param name="SampleRetention">Maximum age of a retained per-observation sample.</param>
/// <param name="RollupRetention">Maximum age of a retained hourly rollup.</param>
/// <param name="EventRetention">Maximum age of a retained durable manager event.</param>
/// <param name="MaximumSamplesPerProfile">Hard per-profile ceiling on retained samples.</param>
/// <param name="MaximumEventsPerProfile">Hard per-profile ceiling on retained manager events.</param>
public sealed record HistoryRetentionPolicy(
    TimeSpan SampleRetention,
    TimeSpan RollupRetention,
    TimeSpan EventRetention,
    int MaximumSamplesPerProfile,
    int MaximumEventsPerProfile);

/// <summary>
/// Describes one retained profile observation.
/// </summary>
/// <remarks>
/// Every nullable measurement keeps an unavailable observation distinct from a measured zero. Local
/// worker counts and control-plane counts are separate evidence and are never collapsed.
/// </remarks>
/// <param name="ObservedAt">Authoritative manager publish time that identifies the sample.</param>
/// <param name="SampledAt">Manager resource-sample time, or <see langword="null"/> when the manager published none.</param>
/// <param name="TelemetryStatus">Manager telemetry status: available, partial, unavailable, or unreported.</param>
/// <param name="ManagerInstanceId">Manager instance that published the observation.</param>
/// <param name="ManagerStatus">Manager lifecycle status at observation time.</param>
/// <param name="Generation">Accepted desired-capacity generation.</param>
/// <param name="DesiredSlots">Slots requested by accepted desired capacity.</param>
/// <param name="ActiveSlots">Slots whose manager process was still running.</param>
/// <param name="DrainingSlots">Active slots removed from desired capacity.</param>
/// <param name="ConfiguredSlots">Configured maximum slot count, or <see langword="null"/> when unreported.</param>
/// <param name="EligibleSlots">Control-plane connected runners, or <see langword="null"/> when unavailable.</param>
/// <param name="TargetSlots">Accepted autoscaling activation target, or <see langword="null"/> for fixed capacity.</param>
/// <param name="MaximumSlots">Configured autoscaling ceiling, or <see langword="null"/> for fixed capacity.</param>
/// <param name="AssignedJobs">Control-plane assigned jobs, or <see langword="null"/> when unavailable.</param>
/// <param name="RunningJobs">Control-plane running jobs, or <see langword="null"/> when unavailable.</param>
/// <param name="AvailableJobs">Control-plane available jobs, or <see langword="null"/> when unavailable.</param>
/// <param name="IdleRunners">Control-plane idle runners, or <see langword="null"/> when unavailable.</param>
/// <param name="BusyRunners">Control-plane busy runners, or <see langword="null"/> when unavailable.</param>
/// <param name="LocalRunningWorkers">Locally observed worker processes the manager still owned.</param>
/// <param name="ManagerCpuCores">Manager process CPU cores, or <see langword="null"/> when unavailable.</param>
/// <param name="ManagerMemoryBytes">Manager process working set, or <see langword="null"/> when unavailable.</param>
/// <param name="ManagerPids">Manager process identifier count, or <see langword="null"/> when unavailable.</param>
/// <param name="HostLogicalProcessorCount">Host logical processors, or <see langword="null"/> when unavailable.</param>
/// <param name="HostMemoryBytes">Host memory capacity, or <see langword="null"/> when unavailable.</param>
/// <param name="WorkerCpuCores">Summed worker CPU cores, or <see langword="null"/> when no worker reported usage.</param>
/// <param name="WorkerMemoryBytes">Summed worker working set, or <see langword="null"/> when no worker reported usage.</param>
/// <param name="WorkerPids">Summed worker process identifiers, or <see langword="null"/> when no worker reported usage.</param>
/// <param name="NetworkRxBytes">Summed cumulative worker received bytes, or <see langword="null"/> when unavailable.</param>
/// <param name="NetworkTxBytes">Summed cumulative worker transmitted bytes, or <see langword="null"/> when unavailable.</param>
/// <param name="BlockReadBytes">Summed cumulative worker block-device bytes read, or <see langword="null"/> when unavailable.</param>
/// <param name="BlockWriteBytes">Summed cumulative worker block-device bytes written, or <see langword="null"/> when unavailable.</param>
/// <param name="ExitReports">Workers whose latest exit evidence was reported.</param>
/// <param name="AdverseExitReports">Reported exits the manager did not classify as clean.</param>
/// <param name="LocalCapacityDeficit">Largest manager-reported local shortfall, or <see langword="null"/> when unavailable.</param>
/// <param name="EligibilityCapacityDeficit">Largest manager-reported eligibility shortfall, or <see langword="null"/> when unavailable.</param>
/// <param name="CapacityDeficitReason">Manager-supplied blocking reason for the largest shortfall, or <see langword="null"/> when unreported.</param>
/// <param name="CapacityDeficitFreshness">Freshness the manager attached to its capacity evidence, or <see langword="null"/> when unreported.</param>
public sealed record ProfileTelemetrySample(
    DateTimeOffset ObservedAt,
    DateTimeOffset? SampledAt,
    string TelemetryStatus,
    string ManagerInstanceId,
    string ManagerStatus,
    int Generation,
    int DesiredSlots,
    int ActiveSlots,
    int DrainingSlots,
    int? ConfiguredSlots,
    int? EligibleSlots,
    int? TargetSlots,
    int? MaximumSlots,
    int? AssignedJobs,
    int? RunningJobs,
    int? AvailableJobs,
    int? IdleRunners,
    int? BusyRunners,
    int LocalRunningWorkers,
    double? ManagerCpuCores,
    long? ManagerMemoryBytes,
    int? ManagerPids,
    int? HostLogicalProcessorCount,
    long? HostMemoryBytes,
    double? WorkerCpuCores,
    long? WorkerMemoryBytes,
    int? WorkerPids,
    long? NetworkRxBytes,
    long? NetworkTxBytes,
    long? BlockReadBytes,
    long? BlockWriteBytes,
    int ExitReports,
    int AdverseExitReports,
    int? LocalCapacityDeficit,
    int? EligibilityCapacityDeficit,
    string? CapacityDeficitReason,
    string? CapacityDeficitFreshness);

/// <summary>
/// Describes one deterministic hourly rollup derived from retained samples.
/// </summary>
/// <remarks>
/// Every aggregate is the maximum retained measurement in the bucket, which is stable for gauges and
/// correct for the cumulative network and block-I/O counters. A <see langword="null"/> aggregate
/// means no sample in the bucket carried the measurement.
/// </remarks>
/// <param name="BucketStart">Inclusive UTC start of the one-hour bucket.</param>
/// <param name="SampleCount">Retained samples aggregated into the bucket.</param>
/// <param name="MaximumDesiredSlots">Largest desired slot count in the bucket.</param>
/// <param name="MaximumActiveSlots">Largest active slot count in the bucket.</param>
/// <param name="MaximumDrainingSlots">Largest draining slot count in the bucket.</param>
/// <param name="MaximumEligibleSlots">Largest control-plane eligible count, or <see langword="null"/>.</param>
/// <param name="MaximumLocalRunningWorkers">Largest locally observed running worker count.</param>
/// <param name="MaximumManagerCpuCores">Largest manager CPU sample, or <see langword="null"/>.</param>
/// <param name="MaximumManagerMemoryBytes">Largest manager working set, or <see langword="null"/>.</param>
/// <param name="MaximumManagerPids">Largest manager process count, or <see langword="null"/>.</param>
/// <param name="MaximumWorkerCpuCores">Largest summed worker CPU sample, or <see langword="null"/>.</param>
/// <param name="MaximumWorkerMemoryBytes">Largest summed worker working set, or <see langword="null"/>.</param>
/// <param name="MaximumWorkerPids">Largest summed worker process count, or <see langword="null"/>.</param>
/// <param name="MaximumNetworkRxBytes">Largest cumulative received bytes, or <see langword="null"/>.</param>
/// <param name="MaximumNetworkTxBytes">Largest cumulative transmitted bytes, or <see langword="null"/>.</param>
/// <param name="MaximumBlockReadBytes">Largest cumulative block-device bytes read, or <see langword="null"/>.</param>
/// <param name="MaximumBlockWriteBytes">Largest cumulative block-device bytes written, or <see langword="null"/>.</param>
/// <param name="MaximumExitReports">Largest reported exit count in the bucket.</param>
/// <param name="MaximumAdverseExitReports">Largest non-clean reported exit count in the bucket.</param>
/// <param name="MaximumLocalCapacityDeficit">Largest manager-reported local shortfall, or <see langword="null"/>.</param>
public sealed record ProfileTelemetryRollup(
    DateTimeOffset BucketStart,
    int SampleCount,
    int MaximumDesiredSlots,
    int MaximumActiveSlots,
    int MaximumDrainingSlots,
    int? MaximumEligibleSlots,
    int MaximumLocalRunningWorkers,
    double? MaximumManagerCpuCores,
    long? MaximumManagerMemoryBytes,
    int? MaximumManagerPids,
    double? MaximumWorkerCpuCores,
    long? MaximumWorkerMemoryBytes,
    int? MaximumWorkerPids,
    long? MaximumNetworkRxBytes,
    long? MaximumNetworkTxBytes,
    long? MaximumBlockReadBytes,
    long? MaximumBlockWriteBytes,
    int MaximumExitReports,
    int MaximumAdverseExitReports,
    int? MaximumLocalCapacityDeficit);

/// <summary>
/// Describes what the dashboard durably retained from one bounded manager journal.
/// </summary>
/// <remarks>
/// A journal gap is explicit rather than inferred: <paramref name="MissedEvents"/> counts durable
/// sequences the manager advanced past between deliveries, and <paramref name="UndeliveredEvents"/>
/// counts sequences the manager reports as retained above the highest sequence delivered so far.
/// </remarks>
/// <param name="Status">Latest manager journal status: current, truncated, unavailable, or unreported.</param>
/// <param name="Capacity">Retention window the manager applies to its own journal.</param>
/// <param name="ManagerHighestSequence">Highest sequence the manager reported retaining.</param>
/// <param name="StoredLowestSequence">Lowest durable sequence the dashboard retained.</param>
/// <param name="StoredHighestSequence">Highest durable sequence the dashboard retained.</param>
/// <param name="ManagerDroppedEvents">Entries the manager reported discarding from its window.</param>
/// <param name="MissedEvents">Durable sequences the manager advanced past without delivering them.</param>
/// <param name="UndeliveredEvents">Retained manager sequences above the highest delivered sequence.</param>
/// <param name="UpdatedAt">Dashboard time the projection last advanced.</param>
public sealed record ProfileEventJournalState(
    string Status,
    int Capacity,
    long? ManagerHighestSequence,
    long? StoredLowestSequence,
    long? StoredHighestSequence,
    int ManagerDroppedEvents,
    long MissedEvents,
    long UndeliveredEvents,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Returns the bounded retained history for one profile.
/// </summary>
/// <param name="ProfileId">Profile identifier local to the connected server.</param>
/// <param name="Samples">Retained samples in ascending observation order.</param>
/// <param name="Rollups">Deterministic hourly rollups in ascending bucket order.</param>
/// <param name="Events">Retained durable manager events, newest first.</param>
/// <param name="PointsTruncated">Whether the point limit hid older points in the requested range.</param>
/// <param name="EventsTruncated">Whether the event limit hid older events in the requested range.</param>
/// <param name="Journal">Explicit journal availability and gap state.</param>
public sealed record ProfileHistory(
    string ProfileId,
    IReadOnlyList<ProfileTelemetrySample> Samples,
    IReadOnlyList<ProfileTelemetryRollup> Rollups,
    IReadOnlyList<ManagerEvent> Events,
    bool PointsTruncated,
    bool EventsTruncated,
    ProfileEventJournalState Journal);

/// <summary>
/// Returns bounded retained history for one tenant node.
/// </summary>
/// <param name="NodeId">Dashboard-assigned node identifier.</param>
/// <param name="GeneratedAt">Dashboard time when the response was generated.</param>
/// <param name="From">Inclusive start of the served range.</param>
/// <param name="To">Exclusive end of the served range.</param>
/// <param name="Resolution">Stored resolution that was served: raw or hourly.</param>
/// <param name="Profiles">Per-profile bounded history.</param>
public sealed record NodeHistoryResponse(
    Guid NodeId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset From,
    DateTimeOffset To,
    string Resolution,
    IReadOnlyList<ProfileHistory> Profiles);

/// <summary>
/// Persists and serves bounded historical runner telemetry and durable manager events.
/// </summary>
public interface IFleetHistoryStore
{
  /// <summary>
  /// Appends bounded history for one connector heartbeat and enforces retention.
  /// </summary>
  /// <remarks>
  /// A sample is appended only when the authoritative manager observation time advances, and a
  /// manager event is appended only once for its durable profile sequence, so a duplicated
  /// connector heartbeat creates neither a duplicate sample nor a duplicate event.
  /// </remarks>
  /// <param name="nodeId">Authenticated node identifier.</param>
  /// <param name="profiles">Latest profile observations carried by the heartbeat.</param>
  /// <param name="receivedAt">Dashboard time when the heartbeat was accepted.</param>
  /// <param name="retention">Retention policy applied after the append.</param>
  /// <param name="cancellationToken">Token that cancels the append.</param>
  /// <returns>A task that completes after history is committed.</returns>
  Task AppendAsync(
      Guid nodeId,
      IReadOnlyList<ManagerObservedState> profiles,
      DateTimeOffset receivedAt,
      HistoryRetentionPolicy retention,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads bounded history for every retained profile of one tenant node.
  /// </summary>
  /// <param name="tenantId">Tenant that must own the node.</param>
  /// <param name="nodeId">Dashboard-assigned node identifier.</param>
  /// <param name="window">Bounded time range, resolution, and point limits.</param>
  /// <param name="generatedAt">Dashboard time used to stamp the response.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The node history, or <see langword="null"/> when the tenant does not own the node.</returns>
  Task<NodeHistoryResponse?> GetNodeHistoryAsync(
      string tenantId,
      Guid nodeId,
      HistoryWindow window,
      DateTimeOffset generatedAt,
      CancellationToken cancellationToken);

  /// <summary>
  /// Loads bounded history for one profile of one tenant node.
  /// </summary>
  /// <param name="tenantId">Tenant that must own the node.</param>
  /// <param name="nodeId">Dashboard-assigned node identifier.</param>
  /// <param name="profileId">Profile identifier local to the connected server.</param>
  /// <param name="window">Bounded time range, resolution, and point limits.</param>
  /// <param name="generatedAt">Dashboard time used to stamp the response.</param>
  /// <param name="cancellationToken">Token that cancels the query.</param>
  /// <returns>The profile history, or <see langword="null"/> when the tenant does not own the node.</returns>
  Task<NodeHistoryResponse?> GetProfileHistoryAsync(
      string tenantId,
      Guid nodeId,
      string profileId,
      HistoryWindow window,
      DateTimeOffset generatedAt,
      CancellationToken cancellationToken);
}
