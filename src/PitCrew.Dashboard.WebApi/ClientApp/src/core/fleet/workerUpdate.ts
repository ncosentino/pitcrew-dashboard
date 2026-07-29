import { type ManagerObservedState } from './fleetApi';

/** Describes worker-image convergence without inferring success from missing evidence. */
export function describeWorkerUpdate(profile: ManagerObservedState): string {
  const update = profile.update;
  if (update === null) return 'No worker-image rollout evidence was reported.';
  if (update.status === 'current') {
    return `${update.currentWorkers} current workers; no stale workers reported.`;
  }
  if (update.status === 'rolling') {
    return `${update.currentWorkers} current and ${update.staleWorkers} stale workers.`;
  }
  return update.lastError ?? 'The manager reported degraded image convergence.';
}
