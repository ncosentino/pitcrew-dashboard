import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const offsetDateTimeSchema = z.string().datetime({ offset: true });

const resourceUsageSchema = z.object({
  cpuCores: z.number().nonnegative(),
  memoryWorkingSetBytes: z.number().int().nonnegative(),
  pids: z.number().int().nonnegative(),
  networkRxBytes: z.number().int().nonnegative().nullable().default(null),
  networkTxBytes: z.number().int().nonnegative().nullable().default(null),
  blockReadBytes: z.number().int().nonnegative().nullable().default(null),
  blockWriteBytes: z.number().int().nonnegative().nullable().default(null),
});

const workerResourcePolicySchema = z
  .object({
    memoryBytes: z.number().int().positive().nullable(),
    memorySwapBytes: z.number().int().positive().nullable(),
    cpuCores: z.string().nullable(),
    pids: z.number().int().positive().nullable(),
  })
  .superRefine((policy, context) => {
    if (
      policy.memoryBytes == null &&
      policy.memorySwapBytes == null &&
      policy.cpuCores == null &&
      policy.pids == null
    ) {
      context.addIssue({
        code: 'custom',
        message: 'A reported resource policy must configure at least one limit.',
        path: ['memoryBytes'],
      });
    }
    if (
      policy.memorySwapBytes != null &&
      (policy.memoryBytes == null || policy.memorySwapBytes < policy.memoryBytes)
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Memory plus swap requires a memory limit and cannot be smaller than it.',
        path: ['memorySwapBytes'],
      });
    }
  });

const workerLastExitSchema = z.object({
  observedAt: offsetDateTimeSchema,
  classification: z.enum([
    'clean',
    'oom-killed',
    'sigkill',
    'signal',
    'error',
    'launch-failure',
    'unknown',
  ]),
  exitCode: z.number().int().min(0).max(255).nullable(),
  signal: z.number().int().min(1).max(64).nullable(),
  dockerOomKilled: z.boolean().nullable(),
  evidence: z.enum(['docker-inspect', 'docker-wait', 'launch', 'unavailable']),
});

const scaleSetStatisticsSchema = z.object({
  observedAt: offsetDateTimeSchema,
  availableJobs: z.number().int().nonnegative(),
  acquiredJobs: z.number().int().nonnegative(),
  assignedJobs: z.number().int().nonnegative(),
  runningJobs: z.number().int().nonnegative(),
  registeredRunners: z.number().int().nonnegative(),
  busyRunners: z.number().int().nonnegative(),
  idleRunners: z.number().int().nonnegative(),
});

const autoscalingTargetSchema = z.object({
  key: z.string().min(1),
  repository: z.string().nullable(),
  maximumSlots: z.number().int().nonnegative(),
  targetSlots: z.number().int().nonnegative(),
  localActiveWorkers: z.number().int().nonnegative(),
  localIdleWorkers: z.number().int().nonnegative(),
  localBusyWorkers: z.number().int().nonnegative(),
  localDrainingWorkers: z.number().int().nonnegative(),
  statistics: scaleSetStatisticsSchema.nullable(),
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
  maximumActiveWorkers: z.number().int().nonnegative().nullable().default(null),
  targets: z.array(autoscalingTargetSchema).nullable().default(null),
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
  imageId: z
    .string()
    .regex(/^sha256:[0-9a-f]{64}$/u, 'Image identity must be an immutable sha256 digest.')
    .nullable()
    .default(null),
  lastExit: workerLastExitSchema.nullable().default(null),
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
    resourcePolicy: workerResourcePolicySchema.nullable().default(null),
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

    if (profile.managerContractVersion >= 11 && profile.autoscaling != null) {
      if (profile.autoscaling.maximumActiveWorkers == null) {
        context.addIssue({
          code: 'custom',
          message: 'Manager contract 11 requires an active-worker admission ceiling.',
          path: ['autoscaling', 'maximumActiveWorkers'],
        });
      }
      if (profile.autoscaling.targets == null) {
        context.addIssue({
          code: 'custom',
          message: 'Manager contract 11 requires per-target scale-set projections.',
          path: ['autoscaling', 'targets'],
        });
      }
    }

    const targets = profile.autoscaling?.targets;
    if (profile.autoscaling != null && targets != null) {
      const totals = targets.reduce(
        (aggregate, target) => ({
          idle: aggregate.idle + target.localIdleWorkers,
          busy: aggregate.busy + target.localBusyWorkers,
          targetSlots: aggregate.targetSlots + target.targetSlots,
        }),
        { idle: 0, busy: 0, targetSlots: 0 },
      );
      if (totals.idle !== profile.autoscaling.idleRunners) {
        context.addIssue({
          code: 'custom',
          message: 'Local idle worker counts must sum to the aggregate idle count.',
          path: ['autoscaling', 'idleRunners'],
        });
      }
      if (totals.busy !== profile.autoscaling.busyRunners) {
        context.addIssue({
          code: 'custom',
          message: 'Local busy worker counts must sum to the aggregate busy count.',
          path: ['autoscaling', 'busyRunners'],
        });
      }
      if (totals.targetSlots !== profile.autoscaling.targetSlots) {
        context.addIssue({
          code: 'custom',
          message: 'Per-target activation must sum to the aggregate activation target.',
          path: ['autoscaling', 'targetSlots'],
        });
      }
    }

    profile.slots.forEach((slot, index) => {
      const lastExit = slot.lastExit;
      if (lastExit == null) return;
      if (lastExit.signal != null && lastExit.exitCode !== 128 + lastExit.signal) {
        context.addIssue({
          code: 'custom',
          message: 'A reported signal must match its 128-based exit code.',
          path: ['slots', index, 'lastExit', 'signal'],
        });
      }
      if ((lastExit.dockerOomKilled === true) !== (lastExit.classification === 'oom-killed')) {
        context.addIssue({
          code: 'custom',
          message: 'Only a Docker-confirmed out-of-memory kill can be classified as oom-killed.',
          path: ['slots', index, 'lastExit', 'classification'],
        });
      }
    });
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
/** Configured manager contract 11 per-worker resource admission policy. */
export type WorkerResourcePolicy = z.infer<typeof workerResourcePolicySchema>;
/** Bounded manager contract 11 exit evidence for one worker identity. */
export type WorkerLastExit = z.infer<typeof workerLastExitSchema>;
/** Timestamped GitHub scale-set statistics for one target. */
export type ScaleSetStatistics = z.infer<typeof scaleSetStatisticsSchema>;
/** One scale-set target with separate local and GitHub evidence. */
export type AutoscalingTarget = z.infer<typeof autoscalingTargetSchema>;
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
