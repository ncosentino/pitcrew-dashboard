import type { FleetResponse, ObservedSlot } from '@/core/fleet';

export interface FleetSlot {
  readonly nodeId: string;
  readonly nodeName: string;
  readonly nodeOnline: boolean;
  readonly profileId: string;
  readonly slot: ObservedSlot;
}

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
