import type { FleetResponse, ObservedSlot } from '@/core/fleet';

export interface FleetSlot {
  readonly nodeId: string;
  readonly nodeName: string;
  readonly nodeOnline: boolean;
  readonly profileId: string;
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

/** Flattens one tenant fleet projection while retaining route context. */
export function flattenFleetSlots(fleet: FleetResponse): ReadonlyArray<FleetSlot> {
  return fleet.nodes.flatMap((node) =>
    node.profiles.flatMap((profile) =>
      profile.slots.map((slot) => ({
        nodeId: node.nodeId,
        nodeName: node.displayName,
        nodeOnline: node.isOnline,
        profileId: profile.profileId,
        slot,
      })),
    ),
  );
}

/** Returns one URL-stable identity for a node/profile/slot tuple. */
export function runnerSelectionId(row: FleetSlot): string {
  return [row.nodeId, row.profileId, row.slot.key].map(encodeURIComponent).join('~');
}

/** Identifies explicit runner state that warrants review without using resource activity. */
export function runnerNeedsReview(row: FleetSlot): boolean {
  return (
    (row.slot.activity === 'busy' && row.slot.currentJob == null) ||
    !row.nodeOnline ||
    row.slot.failureCount > 0 ||
    row.slot.lastExit != null ||
    row.slot.registrationStatus == null ||
    row.slot.registrationStatus === 'disconnected' ||
    row.slot.registrationStatus === 'registration-missing' ||
    row.slot.registrationStatus === 'unknown' ||
    reviewLifecycleStates.has(row.slot.state.toLowerCase()) ||
    row.slot.activity === 'starting' ||
    row.slot.activity === 'draining' ||
    row.slot.activity == null ||
    row.slot.activity === 'unknown' ||
    row.slot.currentJob === undefined
  );
}

/** Orders explicit work and material lifecycle evidence before ordinary idle inventory. */
export function runnerAttentionRank(row: FleetSlot): number {
  if (row.slot.currentJob != null) return 0;
  if (row.slot.activity === 'busy') return 1;
  if (!row.nodeOnline) return 2;
  if (
    row.slot.failureCount > 0 ||
    row.slot.lastExit != null ||
    row.slot.registrationStatus === 'disconnected' ||
    row.slot.registrationStatus === 'registration-missing' ||
    reviewLifecycleStates.has(row.slot.state.toLowerCase())
  ) {
    return 3;
  }
  if (row.slot.activity === 'starting' || row.slot.activity === 'draining') return 4;
  if (runnerNeedsReview(row)) {
    return 5;
  }
  return 100;
}
