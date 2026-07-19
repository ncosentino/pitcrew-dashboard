import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const offsetDateTimeSchema = z.string().datetime({ offset: true });

const observedSlotSchema = z.object({
  key: z.string(),
  repository: z.string().nullable(),
  desired: z.boolean(),
  processRunning: z.boolean(),
  state: z.string(),
  failureCount: z.number().int().nonnegative(),
  backoffSeconds: z.number().int().nonnegative(),
  updatedAt: offsetDateTimeSchema.nullable(),
});

const managerObservedStateSchema = z.object({
  schemaVersion: z.number().int(),
  managerContractVersion: z.number().int(),
  profileId: z.string(),
  managerInstanceId: z.string(),
  managerStatus: z.string(),
  observedAt: offsetDateTimeSchema,
  scope: z.string(),
  generation: z.number().int().nonnegative(),
  desiredStateHash: z.string().nullable(),
  desiredStateStatus: z.string(),
  desiredSlots: z.number().int().nonnegative(),
  activeSlots: z.number().int().nonnegative(),
  drainingSlots: z.number().int().nonnegative(),
  slots: z.array(observedSlotSchema),
});

const fleetNodeSchema = z.object({
  nodeId: z.string().uuid(),
  displayName: z.string(),
  connectorVersion: z.string(),
  enrolledAt: offsetDateTimeSchema,
  lastSeenAt: offsetDateTimeSchema.nullable(),
  isOnline: z.boolean(),
  profiles: z.array(managerObservedStateSchema),
});

const fleetResponseSchema = z.object({
  generatedAt: offsetDateTimeSchema,
  nodes: z.array(fleetNodeSchema),
});

export type ObservedSlot = z.infer<typeof observedSlotSchema>;
export type ManagerObservedState = z.infer<typeof managerObservedStateSchema>;
export type FleetNode = z.infer<typeof fleetNodeSchema>;
export type FleetResponse = z.infer<typeof fleetResponseSchema>;

export async function getFleet(signal: AbortSignal): Promise<FleetResponse> {
  const httpClient = new HttpClient({ baseUrl: globalThis.location.origin });
  return await httpClient.request('/api/fleet/v1/nodes', {
    method: 'GET',
    schema: fleetResponseSchema,
    signal,
  });
}
