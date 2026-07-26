import { formatBytes } from '@/core/formatting/formatters';

import { type AutoscalingTarget, type WorkerLastExit, type WorkerResourcePolicy } from './fleetApi';

/**
 * Number of seconds after which GitHub scale-set statistics are described as stale relative to
 * the manager observation that carried them.
 */
export const staleStatisticsSeconds = 120;

/** Describes how fresh one target's GitHub statistics are relative to the manager observation. */
export type StatisticsFreshness = 'unavailable' | 'stale' | 'current';

/** Summarizes bounded worker exit evidence without implying an unknown exit was clean. */
export interface ExitEvidenceSummary {
  /** Short operator-facing label. */
  readonly label: string;
  /** Accessible sentence describing the evidence and its source. */
  readonly description: string;
  /** Whether the evidence describes a failed or forcibly terminated worker. */
  readonly isFailure: boolean;
}

/** Reports whether a target's GitHub statistics are unavailable, stale, or current. */
export function statisticsFreshness(
  target: AutoscalingTarget,
  observedAt: string,
): StatisticsFreshness {
  if (target.statistics === null) return 'unavailable';
  const ageSeconds =
    (new Date(observedAt).getTime() - new Date(target.statistics.observedAt).getTime()) / 1_000;
  return ageSeconds > staleStatisticsSeconds ? 'stale' : 'current';
}

/**
 * Describes divergence between local worker containers and GitHub registrations without
 * implying that a live container is eligible for work or that a registration is safe to remove.
 */
export function describeTargetDivergence(
  target: AutoscalingTarget,
  freshness: StatisticsFreshness,
): string | null {
  if (target.statistics === null) {
    return 'GitHub statistics are unavailable, so registration divergence cannot be assessed.';
  }
  const registered = target.statistics.registeredRunners;
  const local = target.localActiveWorkers;
  if (registered === local) return null;
  const qualifier = freshness === 'stale' ? ' The GitHub statistics are stale.' : '';
  return registered > local
    ? `GitHub reports ${registered} registered runners while ${local} local worker containers are live. A registration without a local container is not proof that it can be removed.${qualifier}`
    : `${local} local worker containers are live while GitHub reports ${registered} registered runners. A live container is not proof that it is eligible for work.${qualifier}`;
}

/** Summarizes one worker's last-exit evidence, keeping unknown distinct from clean. */
export function describeExitEvidence(
  lastExit: WorkerLastExit | null | undefined,
): ExitEvidenceSummary {
  if (lastExit == null) {
    return {
      label: 'No exit evidence',
      description:
        'No exit evidence has been recorded for this worker, which does not mean it exited cleanly.',
      isFailure: false,
    };
  }

  const parts = [
    lastExit.exitCode == null ? 'exit code unavailable' : `exit code ${lastExit.exitCode}`,
    lastExit.signal == null ? 'no reported signal' : `signal ${lastExit.signal}`,
    lastExit.dockerOomKilled == null
      ? 'Docker did not report an out-of-memory flag'
      : lastExit.dockerOomKilled
        ? 'Docker confirmed an out-of-memory kill'
        : 'Docker reported no out-of-memory kill',
    `evidence from ${lastExit.evidence}`,
  ];
  return {
    label: lastExit.classification,
    description: `Last exit classified as ${lastExit.classification} with ${parts.join(', ')}.`,
    isFailure: lastExit.classification !== 'clean' && lastExit.classification !== 'unknown',
  };
}

/** Formats the configured per-worker resource policy, keeping unconfigured limits explicit. */
export function describeResourcePolicy(
  policy: WorkerResourcePolicy | null | undefined,
): ReadonlyArray<readonly [string, string]> {
  return [
    [
      'Memory',
      policy?.memoryBytes == null ? 'No configured limit' : formatBytes(policy.memoryBytes),
    ],
    [
      'Memory plus swap',
      policy?.memorySwapBytes == null ? 'No configured limit' : formatBytes(policy.memorySwapBytes),
    ],
    ['CPU', policy?.cpuCores == null ? 'No configured limit' : `${policy.cpuCores} cores`],
    ['PIDs', policy?.pids == null ? 'No configured limit' : `${policy.pids} PIDs`],
  ];
}
