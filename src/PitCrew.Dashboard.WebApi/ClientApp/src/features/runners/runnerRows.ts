import type { FleetResponse, ObservedSlot } from '@/core/fleet';

export interface FleetSlot {
  readonly nodeId: string;
  readonly nodeName: string;
  readonly nodeOnline: boolean;
  readonly profileId: string;
  readonly profileManagerStatus: string;
  readonly profileObservedAt: string;
  readonly slot: ObservedSlot;
}

const reviewLifecycleStates = new Set([
  'backoff',
  'degraded',
  'error',
  'failed',
  'stopped',
  'unknown',
]);
const lastKnownManagerStates = new Set(['stale', 'stopped']);

/** Flattens one tenant fleet projection while retaining route context. */
export function flattenFleetSlots(fleet: FleetResponse): ReadonlyArray<FleetSlot> {
  return fleet.nodes.flatMap((node) =>
    node.profiles.flatMap((profile) =>
      profile.slots.map((slot) => ({
        nodeId: node.nodeId,
        nodeName: node.displayName,
        nodeOnline: node.isOnline,
        profileId: profile.profileId,
        profileManagerStatus: profile.managerStatus,
        profileObservedAt: profile.observedAt,
        slot,
      })),
    ),
  );
}

/** Returns one URL-stable identity for a node/profile/slot tuple. */
export function runnerSelectionId(row: FleetSlot): string {
  return [row.nodeId, row.profileId, row.slot.key].map(encodeURIComponent).join('~');
}

/** Returns whether the row's connector and manager can support current evidence claims. */
export function runnerEvidenceIsCurrent(row: FleetSlot): boolean {
  return (
    row.nodeOnline && !lastKnownManagerStates.has(row.profileManagerStatus.toLocaleLowerCase())
  );
}

/** Identifies explicit runner state that warrants review without using resource activity. */
export function runnerNeedsReview(row: FleetSlot): boolean {
  return (
    (row.slot.activity === 'busy' && row.slot.currentJob == null) ||
    !row.nodeOnline ||
    row.profileManagerStatus !== 'running' ||
    row.slot.failureCount > 0 ||
    (row.slot.lastExit != null && row.slot.lastExit.classification !== 'clean') ||
    row.slot.registrationStatus == null ||
    row.slot.registrationStatus === 'disconnected' ||
    row.slot.registrationStatus === 'registration-missing' ||
    row.slot.registrationStatus === 'unknown' ||
    reviewLifecycleStates.has(row.slot.state.toLowerCase()) ||
    row.slot.activity === 'starting' ||
    row.slot.activity === 'draining' ||
    row.slot.activity == null ||
    row.slot.activity === 'unknown'
  );
}

/** Orders explicit work and material lifecycle evidence before ordinary idle inventory. */
export function runnerAttentionRank(row: FleetSlot): number {
  if (!runnerEvidenceIsCurrent(row)) return 2;
  if (row.slot.currentJob != null) return 0;
  if (row.slot.activity === 'busy') return 1;
  if (
    row.slot.failureCount > 0 ||
    (row.slot.lastExit != null && row.slot.lastExit.classification !== 'clean') ||
    row.slot.registrationStatus === 'disconnected' ||
    row.slot.registrationStatus === 'registration-missing' ||
    reviewLifecycleStates.has(row.slot.state.toLowerCase())
  ) {
    return 3;
  }
  if (
    row.slot.activity === 'starting' ||
    row.slot.activity === 'draining' ||
    row.profileManagerStatus === 'starting' ||
    row.profileManagerStatus === 'stopping'
  ) {
    return 4;
  }
  if (runnerNeedsReview(row)) {
    return 5;
  }
  return 100;
}
