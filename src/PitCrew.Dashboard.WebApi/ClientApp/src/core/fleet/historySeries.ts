import type { ProfileHistory, ProfileTelemetryRollup, ProfileTelemetrySample } from './historyApi';

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

/** One recorded change in manager-reported capacity-deficit evidence. */
export interface DeficitReasonChange {
  readonly at: string;
  readonly reason: string | null;
  readonly freshness: string | null;
  readonly localDeficit: number | null;
  readonly eligibilityDeficit: number | null;
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
  readonly unit: HistorySeriesUnit;
  readonly series: readonly SeriesDefinition[];
}

const groupDefinitions: readonly GroupDefinition[] = [
  {
    key: 'capacity',
    label: 'Capacity',
    description:
      'Accepted desired capacity against the slots the manager was still running or draining.',
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
        fromRollup: () => null,
      },
    ],
  },
  {
    key: 'counts',
    label: 'Local workers against control-plane runners',
    description:
      'Locally observed worker processes and GitHub control-plane runner counts are separate evidence and are never collapsed into one number.',
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
        fromRollup: () => null,
      },
      {
        key: 'idle-runners',
        label: 'Control-plane idle runners',
        description: 'Runners GitHub reported as idle.',
        fromSample: (sample) => sample.idleRunners,
        fromRollup: () => null,
      },
      {
        key: 'assigned-jobs',
        label: 'Control-plane assigned jobs',
        description: 'Jobs GitHub reported as assigned to this scale set.',
        fromSample: (sample) => sample.assignedJobs,
        fromRollup: () => null,
      },
    ],
  },
  {
    key: 'cpu',
    label: 'CPU',
    description: 'Manager and summed worker CPU usage sampled by the manager.',
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
        fromRollup: () => null,
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
  return groupDefinitions.map((group) => ({
    key: group.key,
    label: group.label,
    description: group.description,
    unit: group.unit,
    series: group.series.map((series) => ({
      key: series.key,
      label: series.label,
      description: series.description,
      unit: group.unit,
      points:
        resolution === 'hourly'
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

/**
 * Returns only the observations where manager capacity-deficit evidence changed, so that a steady
 * reason is not repeated for every retained sample.
 */
export function buildDeficitReasonChanges(history: ProfileHistory): readonly DeficitReasonChange[] {
  const changes: DeficitReasonChange[] = [];
  let previous: DeficitReasonChange | null = null;
  for (const sample of history.samples) {
    const current: DeficitReasonChange = {
      at: sample.observedAt,
      reason: sample.capacityDeficitReason,
      freshness: sample.capacityDeficitFreshness,
      localDeficit: sample.localCapacityDeficit,
      eligibilityDeficit: sample.eligibilityCapacityDeficit,
    };
    if (
      previous == null ||
      previous.reason !== current.reason ||
      previous.freshness !== current.freshness ||
      previous.localDeficit !== current.localDeficit ||
      previous.eligibilityDeficit !== current.eligibilityDeficit
    ) {
      changes.push(current);
      previous = current;
    }
  }
  return changes.reverse();
}

/** Describes whether the rendered range carries retained points, and whether it was truncated. */
export function describeHistoryAvailability(
  history: ProfileHistory,
  resolution: 'raw' | 'hourly',
): HistoryAvailability {
  const points = resolution === 'hourly' ? history.rollups.length : history.samples.length;
  if (points === 0) {
    return {
      status: 'unavailable',
      label: 'No retained history',
      description:
        'No observation was retained inside this range. History is retained only while a connector reports advancing manager observations.',
    };
  }
  if (history.pointsTruncated) {
    return {
      status: 'partial',
      label: 'Truncated',
      description: `Only the ${points} most recent points inside this range are shown. Narrow the range to see the observations that were hidden.`,
    };
  }
  return {
    status: 'available',
    label: 'Complete',
    description: `All ${points} retained points inside this range are shown.`,
  };
}

/**
 * Describes durable manager-journal gaps explicitly instead of inferring that missing sequences
 * never happened.
 */
export function describeHistoryJournal(history: ProfileHistory): HistoryAvailability {
  const journal = history.journal;
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
