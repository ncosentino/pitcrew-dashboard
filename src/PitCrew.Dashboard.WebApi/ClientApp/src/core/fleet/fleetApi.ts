import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const offsetDateTimeSchema = z.string().datetime({ offset: true });

const resourceUsageSchema = z.object({
  cpuCores: z.number().nonnegative(),
  memoryWorkingSetBytes: z.number().int().nonnegative(),
  pids: z.number().int().nonnegative(),
});

const hostResourceCapacitySchema = z.object({
  logicalProcessorCount: z.number().int().positive(),
  memoryBytes: z.number().int().positive(),
});

const managerResourceTelemetrySchema = z.object({
  sampledAt: offsetDateTimeSchema,
  status: z.enum(['available', 'partial', 'unavailable']),
  host: hostResourceCapacitySchema.nullable(),
  manager: resourceUsageSchema.nullable(),
});

const managerAutoscalingStateSchema = z.object({
  mode: z.literal('scale-set'),
  status: z.enum(['starting', 'running', 'degraded', 'stopping']),
  minimumIdleSlots: z.number().int().nonnegative(),
  maximumSlots: z.number().int().nonnegative(),
  targetSlots: z.number().int().nonnegative(),
  assignedJobs: z.number().int().nonnegative(),
  runningJobs: z.number().int().nonnegative(),
  availableJobs: z.number().int().nonnegative(),
  idleRunners: z.number().int().nonnegative(),
  busyRunners: z.number().int().nonnegative(),
  scaleDownDelaySeconds: z.number().int().nonnegative(),
  scaleSetCount: z.number().int().nonnegative(),
  scaleDownAt: offsetDateTimeSchema.nullable(),
  lastError: z.string().nullable(),
});

const registrationStatusSchema = z.enum([
  'connected',
  'disconnected',
  'registration-missing',
  'unknown',
]);

const observedSlotSchema = z.object({
  key: z.string(),
  repository: z.string().nullable(),
  desired: z.boolean(),
  processRunning: z.boolean(),
  state: z.string(),
  failureCount: z.number().int().nonnegative(),
  backoffSeconds: z.number().int().nonnegative(),
  updatedAt: offsetDateTimeSchema.nullable(),
  resources: resourceUsageSchema.nullable().optional(),
  activity: z.enum(['starting', 'idle', 'busy', 'draining', 'unknown']).nullable().optional(),
  target: z.string().nullable().optional(),
  registrationStatus: registrationStatusSchema.nullable().optional(),
});

const managerObservedStateSchema = z
  .object({
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
    eligibleSlots: z.number().int().nonnegative().nullable().optional(),
    drainingSlots: z.number().int().nonnegative(),
    slots: z.array(observedSlotSchema),
    resourceTelemetry: managerResourceTelemetrySchema.nullable().optional(),
    configuredSlots: z.number().int().nonnegative().nullable().optional(),
    autoscaling: managerAutoscalingStateSchema.nullable().optional(),
  })
  .superRefine((profile, context) => {
    if (profile.managerContractVersion >= 10) {
      if (profile.eligibleSlots == null) {
        context.addIssue({
          code: 'custom',
          message: 'Manager contract 10 requires eligible slot capacity.',
          path: ['eligibleSlots'],
        });
      }
      profile.slots.forEach((slot, index) => {
        if (slot.registrationStatus == null) {
          context.addIssue({
            code: 'custom',
            message: 'Manager contract 10 requires slot registration status.',
            path: ['slots', index, 'registrationStatus'],
          });
        }
      });
    }

    if (profile.eligibleSlots != null) {
      const connectedSlots = profile.slots.filter(
        (slot) => slot.registrationStatus === 'connected',
      ).length;
      if (profile.eligibleSlots !== connectedSlots) {
        context.addIssue({
          code: 'custom',
          message: 'Eligible slot capacity must equal connected slot count.',
          path: ['eligibleSlots'],
        });
      }
    }
  });

const capacityCommandStateSchema = z.object({
  commandId: z.string().uuid(),
  requestedMaximum: z.number().int().positive(),
  status: z.enum(['pending', 'delivered', 'succeeded', 'rejected', 'failed']),
  requestedAt: offsetDateTimeSchema,
  deliveredAt: offsetDateTimeSchema.nullable(),
  completedAt: offsetDateTimeSchema.nullable(),
  resultMessage: z.string().nullable(),
});

const capacityControlStateSchema = z.object({
  profileId: z.string(),
  generation: z.number().int().positive(),
  currentMaximum: z.number().int().positive(),
  maximumAllowed: z.number().int().positive(),
  latestCommand: capacityCommandStateSchema.nullable(),
});

const fleetNodeSchema = z.object({
  nodeId: z.string().uuid(),
  displayName: z.string(),
  connectorVersion: z.string(),
  enrolledAt: offsetDateTimeSchema,
  lastSeenAt: offsetDateTimeSchema.nullable(),
  isOnline: z.boolean(),
  isRevoked: z.boolean(),
  credentialRotationRequested: z.boolean(),
  profiles: z.array(managerObservedStateSchema),
  capacityControls: z.array(capacityControlStateSchema).default([]),
});

const fleetResponseSchema = z.object({
  generatedAt: offsetDateTimeSchema,
  nodes: z.array(fleetNodeSchema),
});

/** Credential-free lifecycle state for one manager slot. */
export type ObservedSlot = z.infer<typeof observedSlotSchema>;
/** Credential-free projection published by one PitCrew manager. */
export type ManagerObservedState = z.infer<typeof managerObservedStateSchema>;
/** Connector-advertised capacity control for one profile. */
export type CapacityControlState = z.infer<typeof capacityControlStateSchema>;
/** One enrolled server and its latest profile projections. */
export type FleetNode = z.infer<typeof fleetNodeSchema>;
/** Current tenant fleet response. */
export type FleetResponse = z.infer<typeof fleetResponseSchema>;

function createClient(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
}

/** Loads the current fleet projection for one authorized tenant. */
export async function getFleet(tenantId: string, signal: AbortSignal): Promise<FleetResponse> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/nodes`,
    {
      method: 'GET',
      schema: fleetResponseSchema,
      signal,
    },
  );
}
