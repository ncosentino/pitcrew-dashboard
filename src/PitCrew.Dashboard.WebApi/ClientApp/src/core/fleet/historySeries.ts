import type {
  HistoryIncompletenessFloor,
  ProfileHistory,
  ProfileTelemetryRollup,
  ProfileTelemetrySample,
} from './historyApi';

/** Measurement unit used to format one history series. */
export type HistorySeriesUnit = 'count' | 'bytes' | 'cores' | 'pids';

/** One point of one history series, where `null` means unavailable rather than zero. */
export interface HistoryPoint {
  readonly at: string;
  readonly value: number | null;
}

/** One named history series with an explicit unit and availability description. */
export interface HistorySeries {
  readonly key: string;
  readonly label: string;
  readonly description: string;
  readonly unit: HistorySeriesUnit;
  readonly points: readonly HistoryPoint[];
}

/** A titled collection of related history series rendered together. */
export interface HistorySeriesGroup {
  readonly key: string;
  readonly label: string;
  readonly description: string;
  readonly unit: HistorySeriesUnit;
  readonly series: readonly HistorySeries[];
}

/** One recorded change in manager-reported capacity-deficit evidence for one target. */
export interface DeficitReasonChange {
  readonly at: string;
  readonly targetKey: string;
  readonly repository: string | null;
  readonly reason: string;
  readonly freshness: string;
  readonly localDeficit: number;
  readonly eligibilityDeficit: number | null;
  readonly evidence: string | null;
}

/** Explicit availability of one rendered history range. */
export interface HistoryAvailability {
  readonly status: 'available' | 'partial' | 'unavailable';
  readonly label: string;
  readonly description: string;
}

type SampleSelector = (sample: ProfileTelemetrySample) => number | null;
type RollupSelector = (rollup: ProfileTelemetryRollup) => number | null;

interface SeriesDefinition {
  readonly key: string;
  readonly label: string;
  readonly description: string;
  readonly fromSample: SampleSelector;
  readonly fromRollup: RollupSelector;
}

interface GroupDefinition {
  readonly key: string;
  readonly label: string;
  readonly description: string;
  readonly hourlyNote: string;
  readonly unit: HistorySeriesUnit;
  readonly series: readonly SeriesDefinition[];
}

const peakNote =
  'Hourly points are the peak value measured inside each whole UTC hour, not an average.';
const cumulativePeakNote =
  'Hourly points are the highest cumulative counter value seen inside each whole UTC hour. They are not per-hour usage, and they reset when workers are replaced.';

const groupDefinitions: readonly GroupDefinition[] = [
  {
    key: 'capacity',
    label: 'Capacity',
    description:
      'Accepted desired capacity against the slots the manager was still running or draining.',
    hourlyNote: peakNote,
    unit: 'count',
    series: [
      {
        key: 'desired-slots',
        label: 'Desired slots',
        description: 'Slots requested by the accepted desired-capacity generation.',
        fromSample: (sample) => sample.desiredSlots,
        fromRollup: (rollup) => rollup.maximumDesiredSlots,
      },
      {
        key: 'active-slots',
        label: 'Active slots',
        description: 'Slots whose manager process was still running.',
        fromSample: (sample) => sample.activeSlots,
        fromRollup: (rollup) => rollup.maximumActiveSlots,
      },
      {
        key: 'draining-slots',
        label: 'Draining slots',
        description: 'Active slots the manager had removed from desired capacity.',
        fromSample: (sample) => sample.drainingSlots,
        fromRollup: (rollup) => rollup.maximumDrainingSlots,
      },
      {
        key: 'target-slots',
        label: 'Autoscaling target',
        description:
          'Accepted autoscaling activation target. Unavailable for fixed-capacity profiles.',
        fromSample: (sample) => sample.targetSlots,
        fromRollup: (rollup) => rollup.maximumTargetSlots,
      },
    ],
  },
  {
    key: 'counts',
    label: 'Local workers against control-plane runners',
    description:
      'Locally observed worker processes and GitHub control-plane runner counts are separate evidence and are never collapsed into one number.',
    hourlyNote: peakNote,
    unit: 'count',
    series: [
      {
        key: 'local-running-workers',
        label: 'Local running workers',
        description: 'Worker processes the manager still owned on this server.',
        fromSample: (sample) => sample.localRunningWorkers,
        fromRollup: (rollup) => rollup.maximumLocalRunningWorkers,
      },
      {
        key: 'eligible-slots',
        label: 'Control-plane connected runners',
        description:
          'Runners GitHub reported as connected. Unavailable when the manager could not read the control plane.',
        fromSample: (sample) => sample.eligibleSlots,
        fromRollup: (rollup) => rollup.maximumEligibleSlots,
      },
      {
        key: 'busy-runners',
        label: 'Control-plane busy runners',
        description: 'Runners GitHub reported as busy.',
        fromSample: (sample) => sample.busyRunners,
        fromRollup: (rollup) => rollup.maximumBusyRunners,
      },
      {
        key: 'idle-runners',
        label: 'Control-plane idle runners',
        description: 'Runners GitHub reported as idle.',
        fromSample: (sample) => sample.idleRunners,
        fromRollup: (rollup) => rollup.maximumIdleRunners,
      },
      {
        key: 'assigned-jobs',
        label: 'Control-plane assigned jobs',
        description: 'Jobs GitHub reported as assigned to this scale set.',
        fromSample: (sample) => sample.assignedJobs,
        fromRollup: (rollup) => rollup.maximumAssignedJobs,
      },
    ],
  },
  {
    key: 'cpu',
    label: 'CPU',
    description: 'Manager and summed worker CPU usage sampled by the manager.',
    hourlyNote: peakNote,
    unit: 'cores',
    series: [
      {
        key: 'manager-cpu',
        label: 'Manager CPU',
        description: 'CPU consumed by the manager process itself.',
        fromSample: (sample) => sample.managerCpuCores,
        fromRollup: (rollup) => rollup.maximumManagerCpuCores,
      },
      {
        key: 'worker-cpu',
        label: 'Worker CPU',
        description:
          'CPU summed across reporting workers. Unavailable when no worker reported usage.',
        fromSample: (sample) => sample.workerCpuCores,
        fromRollup: (rollup) => rollup.maximumWorkerCpuCores,
      },
    ],
  },
  {
    key: 'memory',
    label: 'Memory',
    description: 'Manager and summed worker working-set memory sampled by the manager.',
    hourlyNote: peakNote,
    unit: 'bytes',
    series: [
      {
        key: 'manager-memory',
        label: 'Manager memory',
        description: 'Working set of the manager process itself.',
        fromSample: (sample) => sample.managerMemoryBytes,
        fromRollup: (rollup) => rollup.maximumManagerMemoryBytes,
      },
      {
        key: 'worker-memory',
        label: 'Worker memory',
        description:
          'Working set summed across reporting workers. Unavailable when no worker reported usage.',
        fromSample: (sample) => sample.workerMemoryBytes,
        fromRollup: (rollup) => rollup.maximumWorkerMemoryBytes,
      },
    ],
  },
  {
    key: 'pids',
    label: 'Processes',
    description: 'Manager and summed worker process-identifier counts.',
    hourlyNote: peakNote,
    unit: 'pids',
    series: [
      {
        key: 'manager-pids',
        label: 'Manager PIDs',
        description: 'Process identifiers held by the manager process itself.',
        fromSample: (sample) => sample.managerPids,
        fromRollup: (rollup) => rollup.maximumManagerPids,
      },
      {
        key: 'worker-pids',
        label: 'Worker PIDs',
        description:
          'Process identifiers summed across reporting workers. Unavailable when no worker reported usage.',
        fromSample: (sample) => sample.workerPids,
        fromRollup: (rollup) => rollup.maximumWorkerPids,
      },
    ],
  },
  {
    key: 'network',
    label: 'Network',
    description:
      'Cumulative worker network counters. These only ever increase while the same workers run, and reset when workers are replaced.',
    hourlyNote: cumulativePeakNote,
    unit: 'bytes',
    series: [
      {
        key: 'network-rx',
        label: 'Received',
        description: 'Cumulative bytes received, summed across reporting workers.',
        fromSample: (sample) => sample.networkRxBytes,
        fromRollup: (rollup) => rollup.maximumNetworkRxBytes,
      },
      {
        key: 'network-tx',
        label: 'Transmitted',
        description: 'Cumulative bytes transmitted, summed across reporting workers.',
        fromSample: (sample) => sample.networkTxBytes,
        fromRollup: (rollup) => rollup.maximumNetworkTxBytes,
      },
    ],
  },
  {
    key: 'block-io',
    label: 'Block I/O',
    description:
      'Cumulative worker block-device counters. These only ever increase while the same workers run, and reset when workers are replaced.',
    hourlyNote: cumulativePeakNote,
    unit: 'bytes',
    series: [
      {
        key: 'block-read',
        label: 'Read',
        description: 'Cumulative block-device bytes read, summed across reporting workers.',
        fromSample: (sample) => sample.blockReadBytes,
        fromRollup: (rollup) => rollup.maximumBlockReadBytes,
      },
      {
        key: 'block-write',
        label: 'Written',
        description: 'Cumulative block-device bytes written, summed across reporting workers.',
        fromSample: (sample) => sample.blockWriteBytes,
        fromRollup: (rollup) => rollup.maximumBlockWriteBytes,
      },
    ],
  },
  {
    key: 'exits',
    label: 'Worker exits',
    description:
      'Workers whose latest exit evidence the manager reported, and how many of those exits were not classified as clean.',
    hourlyNote: peakNote,
    unit: 'count',
    series: [
      {
        key: 'exit-reports',
        label: 'Reported exits',
        description: 'Workers carrying manager-reported exit evidence.',
        fromSample: (sample) => sample.exitReports,
        fromRollup: (rollup) => rollup.maximumExitReports,
      },
      {
        key: 'adverse-exit-reports',
        label: 'Adverse exits',
        description: 'Reported exits the manager did not classify as clean.',
        fromSample: (sample) => sample.adverseExitReports,
        fromRollup: (rollup) => rollup.maximumAdverseExitReports,
      },
    ],
  },
  {
    key: 'deficits',
    label: 'Capacity deficits',
    description:
      'Manager-reported shortfalls between requested capacity and the workers or runners that actually appeared.',
    hourlyNote: peakNote,
    unit: 'count',
    series: [
      {
        key: 'local-deficit',
        label: 'Local shortfall',
        description: 'Workers the manager expected locally but did not observe.',
        fromSample: (sample) => sample.localCapacityDeficit,
        fromRollup: (rollup) => rollup.maximumLocalCapacityDeficit,
      },
      {
        key: 'eligibility-deficit',
        label: 'Eligibility shortfall',
        description: 'Local workers that never became eligible in the control plane.',
        fromSample: (sample) => sample.eligibilityCapacityDeficit,
        fromRollup: (rollup) => rollup.maximumEligibilityCapacityDeficit,
      },
    ],
  },
];

/**
 * Projects retained samples or rollups into shared series groups.
 *
 * Series carry `null` where the manager published no measurement so that an unavailable
 * observation is never rendered as a measured zero.
 */
export function buildHistorySeries(
  history: ProfileHistory,
  resolution: 'raw' | 'hourly',
): readonly HistorySeriesGroup[] {
  const isHourly = resolution === 'hourly';
  return groupDefinitions.map((group) => ({
    key: group.key,
    label: isHourly ? `${group.label} peaks` : group.label,
    description: isHourly ? `${group.description} ${group.hourlyNote}` : group.description,
    unit: group.unit,
    series: group.series.map((series) => ({
      key: series.key,
      label: isHourly ? `Peak ${lowerFirst(series.label)}` : series.label,
      description: isHourly ? `${series.description} ${group.hourlyNote}` : series.description,
      unit: group.unit,
      points: isHourly
        ? history.rollups.map((rollup) => ({
            at: rollup.bucketStart,
            value: series.fromRollup(rollup),
          }))
        : history.samples.map((sample) => ({
            at: sample.observedAt,
            value: series.fromSample(sample),
          })),
    })),
  }));
}

function lowerFirst(value: string): string {
  return value.charAt(0).toLowerCase() + value.slice(1);
}

/**
 * Returns the retained target-keyed capacity-deficit observations newest first.
 *
 * The dashboard persists one row per change in manager-reported evidence for every autoscaling
 * target, so no target is collapsed away and a steady reason is not repeated for every heartbeat.
 */
export function buildDeficitReasonChanges(history: ProfileHistory): readonly DeficitReasonChange[] {
  return [...history.capacityDeficits]
    .map((observation) => ({
      at: observation.observedAt,
      targetKey: observation.targetKey,
      repository: observation.repository,
      reason: observation.reason,
      freshness: observation.freshness,
      localDeficit: observation.localDeficit,
      eligibilityDeficit: observation.eligibilityDeficit,
      evidence: observation.evidence,
    }))
    .sort((left, right) => Date.parse(right.at) - Date.parse(left.at));
}

/**
 * Describes why no capacity-deficit evidence is listed, distinguishing an empty range from
 * evidence that dashboard retention already deleted.
 */
export function describeDeficitEvidence(history: ProfileHistory): HistoryAvailability {
  const dropped = history.retention.droppedCapacityDeficits;
  const floor = history.retention.earliestRetainedCapacityDeficit;
  const retentionNote =
    dropped > 0
      ? ` Dashboard retention has already deleted ${dropped} older capacity-deficit observations${floor == null ? '' : `, so nothing before ${floor} is retained`}.`
      : '';
  if (history.capacityDeficits.length > 0 && history.capacityDeficitsTruncated) {
    return {
      status: 'partial',
      label: 'Truncated',
      description: `Only the ${history.capacityDeficits.length} most recent capacity-deficit observations inside this range are shown; older retained observations inside the same range are hidden. Narrow the range or raise the requested diagnostic limit to see them.${retentionNote}`,
    };
  }
  if (history.capacityDeficits.length > 0) {
    return {
      status: dropped > 0 ? 'partial' : 'available',
      label: dropped > 0 ? 'Retention floor' : 'Retained',
      description:
        dropped > 0
          ? `Every retained change in manager-reported capacity-deficit evidence inside this range is listed, for every autoscaling target still retained.${retentionNote}`
          : 'Every change in manager-reported capacity-deficit evidence retained inside this range is listed, for every autoscaling target.',
    };
  }
  if (dropped > 0 || history.retention.droppedSamples > 0) {
    return {
      status: 'partial',
      label: 'Retention floor',
      description: `No capacity-deficit evidence is retained inside this range. Dashboard retention has already deleted older evidence, so this is not proof that no deficit occurred.${retentionNote}`,
    };
  }
  return {
    status: 'unavailable',
    label: 'None retained',
    description:
      'No retained observation inside this range carried manager capacity-deficit evidence.',
  };
}

/**
 * Describes how completely retained subsystem-health changes are shown.
 *
 * Diagnostic rows are bounded by age and by per-profile and node-wide ceilings, and one response is
 * additionally capped by the requested diagnostic limit, so a capped or swept range is never
 * described as a complete record.
 */
export function describeSubsystemHealthEvidence(history: ProfileHistory): HistoryAvailability {
  const dropped = history.retention.droppedSubsystemHealthChanges;
  const floor = history.retention.earliestRetainedSubsystemHealthChange;
  const retentionNote =
    dropped > 0
      ? ` Dashboard retention has already deleted ${dropped} older subsystem health changes${floor == null ? '' : `, so nothing before ${floor} is retained`}.`
      : '';
  if (history.subsystemHealthChanges.length > 0 && history.subsystemHealthTruncated) {
    return {
      status: 'partial',
      label: 'Truncated',
      description: `Only the ${history.subsystemHealthChanges.length} most recent subsystem health changes inside this range are shown; older retained changes inside the same range are hidden. Narrow the range or raise the requested diagnostic limit to see them.${retentionNote}`,
    };
  }

  if (history.subsystemHealthChanges.length > 0) {
    return {
      status: dropped > 0 ? 'partial' : 'available',
      label: dropped > 0 ? 'Retention floor' : 'Retained',
      description:
        dropped > 0
          ? `Every retained observation where manager-reported subsystem health changed is listed.${retentionNote}`
          : 'Only observations where manager-reported subsystem health changed are listed, and every retained change inside this range is shown.',
    };
  }

  if (dropped > 0) {
    return {
      status: 'partial',
      label: 'Retention floor',
      description: `No subsystem health change is retained inside this range. Dashboard retention has already deleted older changes, so this is not proof that subsystem health never changed.${retentionNote}`,
    };
  }
  return {
    status: 'unavailable',
    label: 'None retained',
    description:
      'No retained observation inside this range carried a manager subsystem health change.',
  };
}

/** Describes how completely retained worker-image rollout transitions are shown. */
export function describeWorkerUpdateEvidence(history: ProfileHistory): HistoryAvailability {
  const dropped = history.retention.droppedSamples;
  const retentionNote =
    dropped > 0
      ? ` Dashboard retention has already deleted ${dropped} older telemetry samples, so older rollout transitions may no longer be derivable.`
      : '';
  if (history.workerUpdateChanges.length > 0 && history.workerUpdatesTruncated) {
    return {
      status: 'partial',
      label: 'Truncated',
      description: `Only the ${history.workerUpdateChanges.length} most recent worker-image rollout transitions inside this range are shown. Narrow the range or raise the requested diagnostic limit to see older retained transitions.${retentionNote}`,
    };
  }
  if (history.workerUpdateChanges.length > 0) {
    return {
      status: dropped > 0 ? 'partial' : 'available',
      label: dropped > 0 ? 'Retention floor' : 'Retained',
      description: `Every worker-image rollout transition derivable from retained samples inside this range is listed.${retentionNote}`,
    };
  }
  if (dropped > 0) {
    return {
      status: 'partial',
      label: 'Retention floor',
      description: `No rollout transition is derivable inside this range. Older samples were deleted, so this is not proof that no rollout occurred.${retentionNote}`,
    };
  }
  return {
    status: 'unavailable',
    label: 'None retained',
    description: 'No retained sample inside this range carried a worker-image rollout transition.',
  };
}

/**
 * Resolves the expected spacing between plotted points for one rendered resolution.
 *
 * Hourly buckets are exactly one hour apart. A per-observation range follows the connector
 * heartbeat, so the server-advertised expected cadence is preferred whenever it is known. When it
 * is not, the spacing is estimated from the observed gaps with the lower median rather than the
 * upper median: with an even number of gaps the upper median is itself one of the long gaps, so a
 * three-point series straddling a long outage would treat the outage as the normal cadence and hide
 * the very gap the chart exists to reveal.
 */
export function resolveCadenceMilliseconds(
  history: ProfileHistory,
  resolution: 'raw' | 'hourly',
  expectedRawCadenceSeconds: number | null,
): number | null {
  if (resolution === 'hourly') return 3_600_000;
  if (expectedRawCadenceSeconds != null && expectedRawCadenceSeconds > 0) {
    return expectedRawCadenceSeconds * 1000;
  }
  const times = history.samples
    .map((sample) => Date.parse(sample.observedAt))
    .filter((value) => !Number.isNaN(value))
    .sort((left, right) => left - right);
  if (times.length < 3) return null;
  const deltas: number[] = [];
  for (let index = 1; index < times.length; index += 1) {
    deltas.push(times[index] - times[index - 1]);
  }
  deltas.sort((left, right) => left - right);
  // Lower median: for an even gap count this picks the smaller of the two middle gaps, whereas the
  // usual Math.floor(length / 2) would pick the larger one and swallow a single long outage.
  const median = deltas[Math.floor((deltas.length - 1) / 2)];
  return median > 0 ? median : null;
}

export function describeHistoryAvailability(
  history: ProfileHistory,
  resolution: 'raw' | 'hourly',
): HistoryAvailability {
  const isHourly = resolution === 'hourly';
  const points = isHourly ? history.rollups.length : history.samples.length;
  const floor = isHourly
    ? history.retention.earliestRetainedRollup
    : history.retention.earliestRetainedSample;
  const dropped = isHourly ? history.retention.droppedRollups : history.retention.droppedSamples;
  const retentionNote =
    dropped > 0
      ? ` Dashboard retention has already deleted ${dropped} older ${isHourly ? 'hourly buckets' : 'samples'}${floor == null ? '' : `, so nothing before ${floor} is retained`}.`
      : '';
  const expiredAt = history.retention.historyExpiredAt;
  if (expiredAt != null) {
    return {
      status: points === 0 ? 'unavailable' : 'partial',
      label: 'Expired',
      description: `Dashboard retention expired this profile's history on ${expiredAt}, so this range is incomplete no matter how many points are shown.${retentionNote}`,
    };
  }
  if (points === 0) {
    return {
      status: 'unavailable',
      label: 'No retained history',
      description: `No ${isHourly ? 'hourly bucket' : 'observation'} was retained inside this range. History is retained only while a connector reports advancing manager observations.${retentionNote}`,
    };
  }
  if (history.pointsTruncated) {
    return {
      status: 'partial',
      label: 'Truncated',
      description: `Only the ${points} most recent ${isHourly ? 'hourly buckets' : 'observations'} inside this range are shown; older points inside the same range are hidden. Narrow the range or raise the requested point limit to see them.${retentionNote}`,
    };
  }
  return {
    status: 'available',
    label: 'Complete',
    description: `All ${points} retained ${isHourly ? 'hourly buckets' : 'observations'} inside this range are shown.${retentionNote}`,
  };
}

/**
 * Describes durable manager-journal gaps explicitly instead of inferring that missing sequences
 * never happened.
 */
export function describeHistoryJournal(history: ProfileHistory): HistoryAvailability {
  const journal = history.journal;
  if (journal.status === 'expired') {
    return {
      status: 'unavailable',
      label: 'Expired',
      description:
        'Dashboard retention expired this profile\u2019s manager journal, so retained manager operations are incomplete rather than complete.',
    };
  }
  if (journal.status === 'unreported' || journal.status === 'unavailable') {
    return {
      status: 'unavailable',
      label: 'Unavailable',
      description:
        'This manager published no readable durable journal, so retained manager operations are unavailable rather than absent.',
    };
  }
  const gaps: string[] = [];
  if (journal.missedEvents > 0) {
    gaps.push(
      `${journal.missedEvents} durable sequences advanced past between deliveries and were never retained`,
    );
  }
  if (journal.undeliveredEvents > 0) {
    gaps.push(
      `${journal.undeliveredEvents} sequences the manager still retains have not been delivered yet`,
    );
  }
  if (journal.managerDroppedEvents > 0) {
    gaps.push(`the manager discarded ${journal.managerDroppedEvents} entries from its own window`);
  }
  if (history.eventsTruncated) {
    gaps.push('older retained events are hidden by the requested event limit');
  }
  if (history.retention.droppedEvents > 0) {
    gaps.push(
      `dashboard retention deleted ${history.retention.droppedEvents} older retained events${
        history.retention.earliestRetainedEvent == null
          ? ''
          : `, so nothing before ${history.retention.earliestRetainedEvent} remains`
      }`,
    );
  }
  if (journal.epochResets > 0) {
    gaps.push(
      `the manager journal restarted its sequence ${journal.epochResets} time(s), so earlier and later sequences belong to different journal generations`,
    );
  }
  if (journal.rejectedFutureEvents > 0) {
    gaps.push(
      `${journal.rejectedFutureEvents} events were rejected because they claimed an implausibly future timestamp`,
    );
  }
  if (history.retention.historyExpiredAt != null) {
    gaps.push(
      `dashboard retention expired this profile's history on ${history.retention.historyExpiredAt}`,
    );
  }
  if (gaps.length === 0) {
    return {
      status: 'available',
      label: 'Complete',
      description:
        'Every durable manager sequence the manager reported has been retained inside this range.',
    };
  }
  return {
    status: 'partial',
    label: 'Gaps',
    description: `This chronology has gaps: ${gaps.join('; ')}.`,
  };
}

/**
 * Describes a coarse incompleteness floor the dashboard can no longer attribute to single profiles.
 *
 * Bounded tombstone caps can evict per-profile provenance while a legal query range still reaches
 * the deleted data. The floor keeps the answer honest at a coarser grain instead of letting the
 * range look complete again.
 */
export function describeIncompletenessFloor(floor: HistoryIncompletenessFloor): string {
  const scope = floor.scope === 'node' ? 'this node' : 'this dashboard';
  return `Dashboard retention compacted history for ${scope} between ${floor.earliestExpiredAt} and ${floor.latestExpiredAt}. This range is incomplete: ${floor.expiredProfiles} profile histories expired; ${floor.droppedSamples} samples, ${floor.droppedRollups} hourly buckets, ${floor.droppedEvents} manager events, ${floor.droppedSubsystemHealthChanges} subsystem-health changes, ${floor.droppedCapacityDeficits} capacity-deficit observations, ${floor.droppedHardwareRevisions} hardware revisions, and ${floor.droppedRunnerAssignments} runner assignments were deleted.`;
}
