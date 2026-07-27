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
/// <param name="DiagnosticLimit">Maximum subsystem-health changes or capacity-deficit observations returned per profile.</param>
/// <param name="NodePointLimit">Maximum samples or rollups returned across every profile of the node.</param>
/// <param name="NodeEventLimit">Maximum manager events returned across every profile of the node.</param>
/// <param name="NodeDiagnosticLimit">Combined maximum subsystem-health and capacity-deficit rows returned across every profile of the node.</param>
/// <remarks>
/// <paramref name="DiagnosticLimit"/> is applied separately to subsystem-health changes and to
/// capacity-deficit observations, so neither collection can hide the other, while
/// <paramref name="NodeDiagnosticLimit"/> is one combined budget shared by both collections so the
/// advertised node-wide cap is never doubled.
/// </remarks>
public sealed record HistoryWindow(
    DateTimeOffset From,
    DateTimeOffset To,
    HistoryResolution Resolution,
    int PointLimit,
    int EventLimit,
    int DiagnosticLimit,
    int NodePointLimit,
    int NodeEventLimit,
    int NodeDiagnosticLimit);

/// <summary>
/// Bounds retained history by measured policy.
/// </summary>
/// <param name="SampleRetention">Maximum age of a retained per-observation sample.</param>
/// <param name="RollupRetention">Maximum age of a retained hourly rollup.</param>
/// <param name="EventRetention">Maximum age of a retained durable manager event.</param>
/// <param name="DiagnosticRetention">Maximum age of a retained subsystem-health change or capacity-deficit observation.</param>
/// <param name="MaximumSamplesPerProfile">Hard per-profile ceiling on retained samples.</param>
/// <param name="MaximumEventsPerProfile">Hard per-profile ceiling on retained manager events.</param>
/// <param name="MaximumDiagnosticsPerProfile">Hard per-profile ceiling on retained rows of each diagnostic table.</param>
/// <param name="MaximumSamplesPerNode">Hard node-wide ceiling on retained samples across every profile.</param>
/// <param name="MaximumEventsPerNode">Hard node-wide ceiling on retained manager events across every profile.</param>
/// <param name="MaximumRollupsPerNode">Hard node-wide ceiling on retained hourly rollups across every profile.</param>
/// <param name="MaximumDiagnosticsPerNode">Combined node-wide ceiling shared by retained subsystem-health changes and capacity-deficit observations.</param>
/// <param name="MaximumProfilesPerNode">Hard ceiling on retained profiles, so profile identifier churn cannot accumulate cursors forever.</param>
/// <param name="MaximumSamplesPerDatabase">Hard database-wide ceiling on retained samples across every node.</param>
/// <param name="MaximumRollupsPerDatabase">Hard database-wide ceiling on retained hourly rollups across every node.</param>
/// <param name="MaximumEventsPerDatabase">Hard database-wide ceiling on retained manager events across every node.</param>
/// <param name="MaximumDiagnosticsPerDatabase">Combined database-wide ceiling shared by both retained diagnostic collections.</param>
/// <param name="MaximumProfileHistories">Hard database-wide ceiling on retained profile histories across every node.</param>
/// <param name="MaximumHistoryNodes">Hard database-wide ceiling on how many nodes retain history at once.</param>
/// <param name="GlobalSweepInterval">Smallest gap between two bounded global maintenance sweeps.</param>
public sealed record HistoryRetentionPolicy(
    TimeSpan SampleRetention,
    TimeSpan RollupRetention,
    TimeSpan EventRetention,
    TimeSpan DiagnosticRetention,
    int MaximumSamplesPerProfile,
    int MaximumEventsPerProfile,
    int MaximumDiagnosticsPerProfile,
    int MaximumSamplesPerNode,
    int MaximumEventsPerNode,
    int MaximumRollupsPerNode,
    int MaximumDiagnosticsPerNode,
    int MaximumProfilesPerNode,
    int MaximumSamplesPerDatabase,
    int MaximumRollupsPerDatabase,
    int MaximumEventsPerDatabase,
    int MaximumDiagnosticsPerDatabase,
    int MaximumProfileHistories,
    int MaximumHistoryNodes,
    TimeSpan GlobalSweepInterval);

/// <summary>
/// Bounds one history append in retention and in accepted manager clock skew.
/// </summary>
/// <remarks>
/// A manager observation stamped further ahead than <paramref name="MaximumClockSkew"/> is rejected
/// rather than retained, so a wrong manager clock cannot create unbounded future buckets that
/// ordinary age-based retention would never reach.
/// </remarks>
/// <param name="Retention">Retention applied after the append.</param>
/// <param name="MaximumClockSkew">Largest accepted lead of a manager timestamp over dashboard time.</param>
public sealed record HistoryAppendPolicy(
    HistoryRetentionPolicy Retention,
    TimeSpan MaximumClockSkew);

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
/// Every aggregate is the peak measurement observed in the bucket, not an hourly total or an hourly
/// average. For the cumulative network and block-I/O counters the peak is the highest cumulative
/// reading seen in the bucket, which is not the traffic used during that hour. A
/// <see langword="null"/> aggregate means no sample in the bucket carried the measurement.
/// Aggregates accumulate incrementally as samples arrive, so pruning raw samples never lowers or
/// overwrites a completed bucket.
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
/// <param name="MaximumEligibilityCapacityDeficit">Largest manager-reported eligibility shortfall, or <see langword="null"/>.</param>
/// <param name="MaximumTargetSlots">Largest accepted autoscaling activation target, or <see langword="null"/>.</param>
/// <param name="MaximumAssignedJobs">Largest control-plane assigned job count, or <see langword="null"/>.</param>
/// <param name="MaximumIdleRunners">Largest control-plane idle runner count, or <see langword="null"/>.</param>
/// <param name="MaximumBusyRunners">Largest control-plane busy runner count, or <see langword="null"/>.</param>
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
    int? MaximumLocalCapacityDeficit,
    int? MaximumEligibilityCapacityDeficit,
    int? MaximumTargetSlots,
    int? MaximumAssignedJobs,
    int? MaximumIdleRunners,
    int? MaximumBusyRunners);

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
/// <param name="Epoch">Local durable journal generation, incremented whenever a manager sequence regression proves the journal was lost.</param>
/// <param name="EpochResets">Number of detected manager journal resets for this profile.</param>
/// <param name="RejectedFutureEvents">Manager events rejected because their timestamp exceeded accepted clock skew.</param>
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
    long Epoch,
    long EpochResets,
    long RejectedFutureEvents,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Describes what dashboard retention has already deleted for one profile.
/// </summary>
/// <remarks>
/// A range that reaches below a retained floor is incomplete because the dashboard deleted the
/// older rows, which is different from a manager that never reported them.
/// </remarks>
/// <param name="EarliestRetainedSample">Oldest retained sample observation, or <see langword="null"/> when none is retained.</param>
/// <param name="DroppedSamples">Samples this profile lost to dashboard retention.</param>
/// <param name="EarliestRetainedRollup">Oldest retained hourly bucket, or <see langword="null"/> when none is retained.</param>
/// <param name="DroppedRollups">Hourly buckets this profile lost to dashboard retention.</param>
/// <param name="EarliestRetainedEvent">Oldest retained manager event observation, or <see langword="null"/> when none is retained.</param>
/// <param name="DroppedEvents">Manager events this profile lost to dashboard retention.</param>
/// <param name="EarliestRetainedSubsystemHealthChange">Oldest retained subsystem-health change, or <see langword="null"/> when none is retained.</param>
/// <param name="DroppedSubsystemHealthChanges">Subsystem-health changes this profile lost to dashboard retention.</param>
/// <param name="EarliestRetainedCapacityDeficit">Oldest retained capacity-deficit observation, or <see langword="null"/> when none is retained.</param>
/// <param name="DroppedCapacityDeficits">Capacity-deficit observations this profile lost to dashboard retention.</param>
/// <param name="RejectedFutureSamples">Samples rejected because their timestamp exceeded accepted clock skew.</param>
/// <param name="HistoryExpiredAt">Dashboard time when every retained row of this profile was deliberately expired, or <see langword="null"/> while the profile history is live.</param>
public sealed record ProfileRetentionFloor(
    DateTimeOffset? EarliestRetainedSample,
    long DroppedSamples,
    DateTimeOffset? EarliestRetainedRollup,
    long DroppedRollups,
    DateTimeOffset? EarliestRetainedEvent,
    long DroppedEvents,
    DateTimeOffset? EarliestRetainedSubsystemHealthChange,
    long DroppedSubsystemHealthChanges,
    DateTimeOffset? EarliestRetainedCapacityDeficit,
    long DroppedCapacityDeficits,
    long RejectedFutureSamples,
    DateTimeOffset? HistoryExpiredAt);

/// <summary>
/// Describes one retained change in manager subsystem health.
/// </summary>
/// <remarks>
/// Contract-12 subsystem health is retained on change, so a steady subsystem costs one row while a
/// failing subsystem keeps its full success, failure, and backoff evidence.
/// </remarks>
/// <param name="Subsystem">Manager subsystem: docker or github.</param>
/// <param name="ObservedAt">Manager time the subsystem state was observed.</param>
/// <param name="State">Manager-reported subsystem state.</param>
/// <param name="ConsecutiveFailures">Consecutive failures the manager counted for the subsystem.</param>
/// <param name="RetryAt">Manager-reported backoff expiry, or <see langword="null"/> when not backing off.</param>
/// <param name="LastSuccessOperation">Operation of the last successful subsystem call, or <see langword="null"/>.</param>
/// <param name="LastSuccessObservedAt">Manager time of the last successful subsystem call, or <see langword="null"/>.</param>
/// <param name="LastSuccessReason">Manager-supplied reason for the last success, or <see langword="null"/>.</param>
/// <param name="LastFailureOperation">Operation of the last failed subsystem call, or <see langword="null"/>.</param>
/// <param name="LastFailureObservedAt">Manager time of the last failed subsystem call, or <see langword="null"/>.</param>
/// <param name="LastFailureReason">Manager-supplied reason for the last failure, or <see langword="null"/>.</param>
/// <param name="LastFailureEvidence">Bounded manager evidence for the last failure, or <see langword="null"/>.</param>
public sealed record ProfileSubsystemHealthChange(
    string Subsystem,
    DateTimeOffset ObservedAt,
    string State,
    int ConsecutiveFailures,
    DateTimeOffset? RetryAt,
    string? LastSuccessOperation,
    DateTimeOffset? LastSuccessObservedAt,
    string? LastSuccessReason,
    string? LastFailureOperation,
    DateTimeOffset? LastFailureObservedAt,
    string? LastFailureReason,
    string? LastFailureEvidence);

/// <summary>
/// Describes one retained change in manager capacity-deficit evidence for one target.
/// </summary>
/// <remarks>
/// Every autoscaling target keeps its own retained chronology; targets are never collapsed into one
/// selected deficit. Fixed-capacity evidence is retained under the reserved
/// <c>fixed</c> target key.
/// </remarks>
/// <param name="TargetKey">Autoscaling target key, or <c>fixed</c> for fixed-capacity evidence.</param>
/// <param name="ObservedAt">Manager time the evidence was observed.</param>
/// <param name="Repository">Repository the target scales, or <see langword="null"/> when unreported.</param>
/// <param name="Freshness">Freshness the manager attached to the evidence.</param>
/// <param name="TargetSlots">Slots the manager was trying to reach for the target.</param>
/// <param name="ActiveWorkers">Workers the manager observed as active.</param>
/// <param name="StartingWorkers">Workers the manager observed as starting.</param>
/// <param name="DrainingWorkers">Workers the manager observed as draining.</param>
/// <param name="CleanupPendingWorkers">Workers the manager observed as pending cleanup.</param>
/// <param name="EligibleWorkers">Workers the control plane reported as eligible, or <see langword="null"/> when unavailable.</param>
/// <param name="LocalDeficit">Workers the manager expected locally but did not observe.</param>
/// <param name="EligibilityDeficit">Local workers that never became eligible, or <see langword="null"/> when unavailable.</param>
/// <param name="Reason">Manager-supplied blocking reason.</param>
/// <param name="Evidence">Bounded manager evidence, or <see langword="null"/> when unreported.</param>
public sealed record ProfileCapacityDeficitObservation(
    string TargetKey,
    DateTimeOffset ObservedAt,
    string? Repository,
    string Freshness,
    int TargetSlots,
    int ActiveWorkers,
    int StartingWorkers,
    int DrainingWorkers,
    int CleanupPendingWorkers,
    int? EligibleWorkers,
    int LocalDeficit,
    int? EligibilityDeficit,
    string Reason,
    string? Evidence);

/// <summary>
/// Returns the bounded retained history for one profile.
/// </summary>
/// <param name="ProfileId">Profile identifier local to the connected server.</param>
/// <param name="Samples">Retained samples in ascending observation order.</param>
/// <param name="Rollups">Deterministic hourly rollups in ascending bucket order.</param>
/// <param name="Events">Retained durable manager events, newest first.</param>
/// <param name="PointsTruncated">Whether the point limit hid older points in the requested range.</param>
/// <param name="EventsTruncated">Whether the event limit hid older events in the requested range.</param>
/// <param name="SubsystemHealthTruncated">Whether the diagnostic limit hid older subsystem-health changes in the requested range.</param>
/// <param name="CapacityDeficitsTruncated">Whether the diagnostic limit hid older capacity-deficit observations in the requested range.</param>
/// <param name="SubsystemHealthChanges">Retained contract-12 subsystem health changes, newest first.</param>
/// <param name="CapacityDeficits">Retained per-target capacity-deficit evidence changes, newest first.</param>
/// <param name="Journal">Explicit journal availability and gap state.</param>
/// <param name="Retention">What dashboard retention already deleted for the profile.</param>
public sealed record ProfileHistory(
    string ProfileId,
    IReadOnlyList<ProfileTelemetrySample> Samples,
    IReadOnlyList<ProfileTelemetryRollup> Rollups,
    IReadOnlyList<ManagerEvent> Events,
    IReadOnlyList<ProfileSubsystemHealthChange> SubsystemHealthChanges,
    IReadOnlyList<ProfileCapacityDeficitObservation> CapacityDeficits,
    bool PointsTruncated,
    bool EventsTruncated,
    bool SubsystemHealthTruncated,
    bool CapacityDeficitsTruncated,
    ProfileEventJournalState Journal,
    ProfileRetentionFloor Retention);

/// <summary>
/// Returns bounded retained history for one tenant node.
/// </summary>
/// <param name="NodeId">Dashboard-assigned node identifier.</param>
/// <param name="GeneratedAt">Dashboard time when the response was generated.</param>
/// <param name="From">Inclusive start of the served range.</param>
/// <param name="To">Exclusive end of the served range.</param>
/// <param name="Resolution">Stored resolution that was served: raw or hourly.</param>
/// <param name="Profiles">Per-profile bounded history.</param>
/// <param name="PointsTruncated">Whether a point ceiling hid older points in the response.</param>
/// <param name="EventsTruncated">Whether an event ceiling hid older events in the response.</param>
/// <param name="DiagnosticsTruncated">Whether a diagnostic ceiling hid older subsystem-health or capacity-deficit rows in the response.</param>
/// <param name="ProfilePointLimit">Per-profile point ceiling applied to the response.</param>
/// <param name="ProfileEventLimit">Per-profile event ceiling applied to the response.</param>
/// <param name="ProfileSubsystemHealthLimit">Per-profile subsystem-health ceiling applied to the response.</param>
/// <param name="ProfileCapacityDeficitLimit">Per-profile capacity-deficit ceiling applied to the response.</param>
/// <param name="NodePointLimit">Node-wide point ceiling applied to the response.</param>
/// <param name="NodeEventLimit">Node-wide event ceiling applied to the response.</param>
/// <param name="NodeDiagnosticLimit">Combined node-wide ceiling shared by both diagnostic collections.</param>
public sealed record NodeHistoryResponse(
    Guid NodeId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset From,
    DateTimeOffset To,
    string Resolution,
    IReadOnlyList<ProfileHistory> Profiles,
    bool PointsTruncated,
    bool EventsTruncated,
    bool DiagnosticsTruncated,
    int ProfilePointLimit,
    int ProfileEventLimit,
    int ProfileSubsystemHealthLimit,
    int ProfileCapacityDeficitLimit,
    int NodePointLimit,
    int NodeEventLimit,
    int NodeDiagnosticLimit);

/// <summary>
/// Advertises the bounded history query shapes one dashboard deployment can answer.
/// </summary>
/// <remarks>
/// Presets are built from these advertised bounds instead of hard-coded client constants, so a
/// deployment configured with a narrower maximum range or lower caps never offers a preset the
/// server rejects.
/// </remarks>
/// <param name="DefaultRangeHours">Range served when a caller supplies no bounds.</param>
/// <param name="MaximumRangeHours">Widest range one bounded query may request.</param>
/// <param name="Resolutions">Stored resolutions this deployment serves.</param>
/// <param name="MaximumPoints">Largest accepted per-profile point limit.</param>
/// <param name="MaximumEvents">Largest accepted per-profile event limit.</param>
/// <param name="MaximumDiagnostics">Largest accepted per-profile diagnostic limit.</param>
/// <param name="NodePointLimit">Node-wide point ceiling this deployment enforces.</param>
/// <param name="NodeEventLimit">Node-wide event ceiling this deployment enforces.</param>
/// <param name="NodeDiagnosticLimit">Combined node-wide diagnostic ceiling this deployment enforces.</param>
/// <param name="ExpectedRawCadenceSeconds">Expected spacing between per-observation samples, taken from the advertised connector poll interval.</param>
/// <param name="SampleRetentionHours">How long a per-observation sample is retained.</param>
/// <param name="RollupRetentionHours">How long an hourly rollup is retained.</param>
public sealed record HistoryCapabilities(
    int DefaultRangeHours,
    int MaximumRangeHours,
    IReadOnlyList<string> Resolutions,
    int MaximumPoints,
    int MaximumEvents,
    int MaximumDiagnostics,
    int NodePointLimit,
    int NodeEventLimit,
    int NodeDiagnosticLimit,
    int ExpectedRawCadenceSeconds,
    int SampleRetentionHours,
    int RollupRetentionHours);

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
  /// manager event is appended only once for its durable journal epoch and profile sequence, so a
  /// duplicated connector heartbeat creates neither a duplicate sample nor a duplicate event. The
  /// append enlists in the caller's transaction so history and latest state commit together.
  /// </remarks>
  /// <param name="transaction">Transaction that also carries the latest-state write for the heartbeat.</param>
  /// <param name="nodeId">Authenticated node identifier.</param>
  /// <param name="profiles">Latest profile observations carried by the heartbeat.</param>
  /// <param name="receivedAt">Dashboard time when the heartbeat was accepted.</param>
  /// <param name="policy">Retention and clock-skew policy applied to the append.</param>
  /// <param name="cancellationToken">Token that cancels the append.</param>
  /// <returns>A task that completes after history is written into the transaction.</returns>
  Task AppendAsync(
      IFleetStorageTransaction transaction,
      Guid nodeId,
      IReadOnlyList<ManagerObservedState> profiles,
      DateTimeOffset receivedAt,
      HistoryAppendPolicy policy,
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
