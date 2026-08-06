import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

export const offsetDateTimeSchema = z.string().datetime({ offset: true });

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

const hostPressureSchema = z
  .object({
    status: z.enum(['available', 'partial', 'unavailable']),
    source: z.literal('docker-host'),
    cpuUtilizationPercent: z.number().min(0).max(100).nullable(),
    load1: z.number().nonnegative().nullable(),
    load5: z.number().nonnegative().nullable(),
    load15: z.number().nonnegative().nullable(),
    memoryTotalBytes: z.number().int().positive().nullable(),
    memoryAvailableBytes: z.number().int().nonnegative().nullable(),
    swapUsedBytes: z.number().int().nonnegative().nullable(),
    cpuPressureSomeAvg10: z.number().min(0).max(100).nullable(),
    cpuPressureFullAvg10: z.number().min(0).max(100).nullable(),
    memoryPressureSomeAvg10: z.number().min(0).max(100).nullable(),
    memoryPressureFullAvg10: z.number().min(0).max(100).nullable(),
    ioPressureSomeAvg10: z.number().min(0).max(100).nullable(),
    ioPressureFullAvg10: z.number().min(0).max(100).nullable(),
  })
  .superRefine((pressure, context) => {
    if (
      pressure.memoryTotalBytes != null &&
      pressure.memoryAvailableBytes != null &&
      pressure.memoryAvailableBytes > pressure.memoryTotalBytes
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Available memory cannot exceed total Docker-host memory.',
        path: ['memoryAvailableBytes'],
      });
    }
    const measurements = [
      pressure.cpuUtilizationPercent,
      pressure.load1,
      pressure.load5,
      pressure.load15,
      pressure.memoryTotalBytes,
      pressure.memoryAvailableBytes,
      pressure.swapUsedBytes,
      pressure.cpuPressureSomeAvg10,
      pressure.cpuPressureFullAvg10,
      pressure.memoryPressureSomeAvg10,
      pressure.memoryPressureFullAvg10,
      pressure.ioPressureSomeAvg10,
      pressure.ioPressureFullAvg10,
    ];
    const coreAvailable = measurements.slice(0, 7).every((value) => value != null);
    if (
      (pressure.status === 'available' && !coreAvailable) ||
      (pressure.status === 'partial' &&
        (coreAvailable || !measurements.some((value) => value != null))) ||
      (pressure.status === 'unavailable' && measurements.some((value) => value != null))
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Docker-host pressure status conflicts with measured fields.',
        path: ['status'],
      });
    }
  });

const hardwareArchitectureSchema = z
  .string()
  .min(1)
  .max(64)
  .refine((value) =>
    Array.from(value).every((character) => {
      const codePoint = character.codePointAt(0) ?? 0;
      return codePoint >= 0x20 && codePoint !== 0x7f;
    }),
  );

export const hostHardwareInventorySchema = z.object({
  status: z.enum(['current', 'stale', 'unavailable']),
  collectedAt: offsetDateTimeSchema.nullable(),
  attemptedAt: offsetDateTimeSchema,
  inventoryHash: z
    .string()
    .regex(/^[0-9a-f]{64}$/u)
    .nullable(),
  processorModel: z.string().min(1).max(256).nullable(),
  architecture: hardwareArchitectureSchema.nullable(),
  physicalCoreCount: z.number().int().positive().nullable(),
  logicalProcessorCount: z.number().int().positive().nullable(),
  performanceCoreCount: z.number().int().positive().nullable(),
  efficiencyCoreCount: z.number().int().positive().nullable(),
  memoryBytes: z.number().int().positive().nullable(),
  operatingSystem: z.string().min(1).max(256).nullable(),
  kernelVersion: z.string().min(1).max(256).nullable(),
  dockerServerVersion: z.string().min(1).max(256).nullable(),
  dockerStorageDriver: z.string().min(1).max(256).nullable(),
  dockerBackingFilesystem: z.string().min(1).max(256).nullable(),
});

const observedHostSchema = z.object({
  hardware: hostHardwareInventorySchema,
});

const managerResourceTelemetrySchema = z.object({
  sampledAt: offsetDateTimeSchema,
  status: z.enum(['available', 'partial', 'unavailable']),
  host: hostResourceCapacitySchema.nullable(),
  manager: resourceUsageSchema.nullable(),
  hostPressure: hostPressureSchema.nullable().optional(),
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

const managerWorkerUpdateStateSchema = z.object({
  status: z.enum(['current', 'rolling', 'degraded']),
  targetImage: z.string().min(1).max(2048).regex(/^\S+$/u).nullable(),
  targetImageId: z
    .string()
    .regex(/^sha256:[0-9a-f]{64}$/u, 'Image identity must be an immutable sha256 digest.')
    .nullable(),
  targetRevision: z.string().regex(/^[0-9a-f]{64}$/u),
  currentWorkers: z.number().int().nonnegative(),
  staleWorkers: z.number().int().nonnegative(),
  lastError: z.string().max(512).nullable(),
});

const registrationStatusSchema = z.enum([
  'connected',
  'disconnected',
  'registration-missing',
  'unknown',
]);

export const currentJobSchema = z.object({
  repository: z
    .string()
    .regex(/^https:\/\/github\.com\/[A-Za-z0-9._-]{1,39}\/[A-Za-z0-9._-]{1,100}$/u),
  workflowRunId: z.number().int().positive(),
  jobId: z.string().regex(/^[1-9][0-9]{0,31}$/u),
  displayName: z.string().min(1).max(256).nullable(),
  eventName: z.string().min(1).max(64).nullable(),
  queuedAt: offsetDateTimeSchema.nullable(),
  scaleSetAssignedAt: offsetDateTimeSchema.nullable(),
  runnerAssignedAt: offsetDateTimeSchema.nullable(),
  startedAt: offsetDateTimeSchema,
  finishedAt: offsetDateTimeSchema.nullable(),
  result: z.string().min(1).max(64).nullable(),
});

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
  runnerNameHash: z
    .string()
    .regex(/^[0-9a-f]{64}$/u)
    .nullable()
    .default(null),
  currentJob: currentJobSchema.nullable().optional(),
});

const managerEventSubsystemSchema = z.enum([
  'docker',
  'registration',
  'scale-set-session',
  'listener',
  'jit',
  'worker-launch',
  'worker-exit',
  'telemetry',
  'reconciliation',
  'cleanup',
  'admission',
  'recovery',
]);

const managerEventOperationSchema = z.enum([
  'docker-ping',
  'docker-run',
  'docker-inspect',
  'docker-remove',
  'docker-events',
  'registration-token-request',
  'runner-registration',
  'runner-removal',
  'session-create',
  'session-refresh',
  'session-delete',
  'message-poll',
  'message-acknowledge',
  'jit-config-generate',
  'worker-launch',
  'worker-exit',
  'telemetry-sample',
  'desired-state-load',
  'desired-state-apply',
  'capacity-acknowledge',
  'observed-state-publish',
  'registration-cleanup',
  'container-cleanup',
  'admission-reserve',
  'admission-settle',
  'manager-start',
  'manager-shutdown',
  'journal-restore',
]);

const managerEventOutcomeSchema = z.enum([
  'succeeded',
  'failed',
  'timed-out',
  'retry-scheduled',
  'blocked',
  'recovered',
  'unknown',
]);

const managerEventReasonSchema = z.enum([
  'none',
  'docker-unavailable',
  'docker-failed',
  'timeout',
  'rate-limited',
  'authorization-failed',
  'not-found',
  'conflict',
  'invalid-state',
  'capacity-ceiling',
  'retry-backoff',
  'cancelled',
  'recovered',
  'unknown',
]);

const sanitizedEvidenceSchema = z.string().max(160).nullable();

export const managerEventSchema = z.object({
  sequence: z.number().int().positive(),
  managerInstanceId: z.string().min(1).max(128),
  observedAt: offsetDateTimeSchema,
  subsystem: managerEventSubsystemSchema,
  operation: managerEventOperationSchema,
  target: z.string().min(1).max(128).nullable(),
  outcome: managerEventOutcomeSchema,
  durationMilliseconds: z.number().int().nonnegative().nullable(),
  attempt: z.number().int().positive().nullable(),
  consecutiveFailures: z.number().int().nonnegative().nullable(),
  retryAt: offsetDateTimeSchema.nullable(),
  reason: managerEventReasonSchema,
  evidence: sanitizedEvidenceSchema,
});

const managerOperationJournalSchema = z.object({
  status: z.enum(['current', 'truncated', 'unavailable']),
  capacity: z.number().int().positive().max(64),
  highestSequence: z.number().int().positive().nullable(),
  droppedEvents: z.number().int().nonnegative(),
  events: z.array(managerEventSchema).max(64),
});

const subsystemOperationEvidenceSchema = z.object({
  operation: managerEventOperationSchema,
  observedAt: offsetDateTimeSchema,
  durationMilliseconds: z.number().int().nonnegative().nullable(),
  reason: managerEventReasonSchema,
  evidence: sanitizedEvidenceSchema,
});

const subsystemHealthSummarySchema = z.object({
  state: z.enum(['healthy', 'degraded', 'unavailable', 'unknown']),
  observedAt: offsetDateTimeSchema,
  consecutiveFailures: z.number().int().nonnegative(),
  retryAt: offsetDateTimeSchema.nullable(),
  lastSuccess: subsystemOperationEvidenceSchema.nullable(),
  lastFailure: subsystemOperationEvidenceSchema.nullable(),
});

const managerSubsystemHealthSchema = z.object({
  docker: subsystemHealthSummarySchema,
  github: subsystemHealthSummarySchema,
});

const capacityDeficitReasonSchema = z.enum([
  'none',
  'admission-ceiling',
  'launch-pending',
  'docker-unavailable',
  'docker-failed',
  'jit-pending',
  'jit-failed',
  'listener-unavailable',
  'session-unavailable',
  'registration-cleanup-pending',
  'worker-draining',
  'invalid-desired-state',
  'retry-backoff',
  'unknown',
]);

const capacityDeficitFields = {
  observedAt: offsetDateTimeSchema,
  freshness: z.enum(['current', 'stale', 'unavailable']),
  targetSlots: z.number().int().nonnegative(),
  activeWorkers: z.number().int().nonnegative(),
  startingWorkers: z.number().int().nonnegative(),
  drainingWorkers: z.number().int().nonnegative(),
  cleanupPendingWorkers: z.number().int().nonnegative(),
  eligibleWorkers: z.number().int().nonnegative().nullable(),
  localDeficit: z.number().int().nonnegative(),
  eligibilityDeficit: z.number().int().nonnegative().nullable(),
  reason: capacityDeficitReasonSchema,
  evidence: sanitizedEvidenceSchema,
};

const capacityDeficitEvidenceSchema = z.object(capacityDeficitFields);

const targetCapacityDeficitEvidenceSchema = z.object({
  key: z.string().min(1).max(128),
  repository: z.string().nullable(),
  ...capacityDeficitFields,
});

const managerCapacityEvidenceSchema = z.object({
  fixed: capacityDeficitEvidenceSchema.nullable(),
  targets: z.array(targetCapacityDeficitEvidenceSchema).max(64),
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
    operationJournal: managerOperationJournalSchema.nullable().default(null),
    subsystemHealth: managerSubsystemHealthSchema.nullable().default(null),
    capacityEvidence: managerCapacityEvidenceSchema.nullable().default(null),
    update: managerWorkerUpdateStateSchema.nullable().default(null),
    host: observedHostSchema.nullable().optional(),
  })
  .superRefine((profile, context) => {
    const hardware = profile.host?.hardware;
    if (hardware != null) {
      const values = [
        hardware.processorModel,
        hardware.architecture,
        hardware.physicalCoreCount,
        hardware.logicalProcessorCount,
        hardware.performanceCoreCount,
        hardware.efficiencyCoreCount,
        hardware.memoryBytes,
        hardware.operatingSystem,
        hardware.kernelVersion,
        hardware.dockerServerVersion,
        hardware.dockerStorageDriver,
        hardware.dockerBackingFilesystem,
      ];
      if (hardware.status === 'unavailable') {
        if (
          hardware.collectedAt != null ||
          hardware.inventoryHash != null ||
          values.some((value) => value != null)
        ) {
          context.addIssue({
            code: 'custom',
            message: 'Unavailable hardware cannot retain inventory values.',
            path: ['host', 'hardware', 'status'],
          });
        }
      } else if (
        hardware.collectedAt == null ||
        hardware.inventoryHash == null ||
        !values.some((value) => value != null)
      ) {
        context.addIssue({
          code: 'custom',
          message: 'Current or stale hardware requires a retained inventory.',
          path: ['host', 'hardware'],
        });
      }
      if (Date.parse(hardware.attemptedAt) > Date.parse(profile.observedAt)) {
        context.addIssue({
          code: 'custom',
          message: 'Hardware collection cannot postdate the manager observation.',
          path: ['host', 'hardware', 'attemptedAt'],
        });
      }
    }
    const runnerNameHashes = profile.slots
      .map((slot) => slot.runnerNameHash)
      .filter((hash): hash is string => hash != null);
    if (new Set(runnerNameHashes).size !== runnerNameHashes.length) {
      context.addIssue({
        code: 'custom',
        message: 'One runner-name hash identifies exactly one live slot.',
        path: ['slots'],
      });
    }
    if (profile.managerContractVersion < 14 && runnerNameHashes.length > 0) {
      context.addIssue({
        code: 'custom',
        message: 'Runner-name hashes require manager contract 14.',
        path: ['slots'],
      });
    }
    profile.slots.forEach((slot, index) => {
      if (profile.managerContractVersion >= 15 && slot.currentJob === undefined) {
        context.addIssue({
          code: 'custom',
          message: 'Manager contract 15 requires explicit current-job availability.',
          path: ['slots', index, 'currentJob'],
        });
      }
      if (profile.managerContractVersion < 15 && slot.currentJob != null) {
        context.addIssue({
          code: 'custom',
          message: 'Current-job context requires manager contract 15.',
          path: ['slots', index, 'currentJob'],
        });
      }
      if (
        slot.currentJob != null &&
        (!slot.processRunning ||
          slot.runnerNameHash == null ||
          (slot.activity !== 'busy' && slot.activity !== 'draining') ||
          (slot.currentJob.result != null && slot.currentJob.finishedAt == null))
      ) {
        context.addIssue({
          code: 'custom',
          message: 'Current-job context requires one live busy or draining correlated slot.',
          path: ['slots', index, 'currentJob'],
        });
      }
      const timestamps = slot.currentJob
        ? [
            slot.currentJob.queuedAt,
            slot.currentJob.scaleSetAssignedAt,
            slot.currentJob.runnerAssignedAt,
            slot.currentJob.startedAt,
            slot.currentJob.finishedAt,
          ].filter((value): value is string => value != null)
        : [];
      if (
        timestamps.some(
          (value, timestampIndex) =>
            timestampIndex > 0 && Date.parse(value) < Date.parse(timestamps[timestampIndex - 1]),
        ) ||
        timestamps.some((value) => Date.parse(value) > Date.parse(profile.observedAt))
      ) {
        context.addIssue({
          code: 'custom',
          message: 'Current-job lifecycle timestamps must be ordered within the observation.',
          path: ['slots', index, 'currentJob'],
        });
      }
    });
    const hostPressure = profile.resourceTelemetry?.hostPressure;
    if (profile.managerContractVersion >= 16 && hostPressure == null) {
      context.addIssue({
        code: 'custom',
        message: 'Manager contract 16 requires Docker-host pressure.',
        path: ['resourceTelemetry', 'hostPressure'],
      });
    }
    if (profile.managerContractVersion < 16 && hostPressure != null) {
      context.addIssue({
        code: 'custom',
        message: 'Docker-host pressure requires manager contract 16.',
        path: ['resourceTelemetry', 'hostPressure'],
      });
    }

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

      const update = profile.update;
      if (update != null) {
        if (update.currentWorkers + update.staleWorkers !== profile.activeSlots) {
          context.addIssue({
            code: 'custom',
            message: 'Current and stale rollout workers must equal active slots.',
            path: ['update', 'currentWorkers'],
          });
        }
        if (update.status === 'current' && update.staleWorkers !== 0) {
          context.addIssue({
            code: 'custom',
            message: 'A current rollout cannot retain stale workers.',
            path: ['update', 'staleWorkers'],
          });
        }
        if (update.status === 'rolling' && update.staleWorkers === 0) {
          context.addIssue({
            code: 'custom',
            message: 'A rolling rollout must retain at least one stale worker.',
            path: ['update', 'staleWorkers'],
          });
        }
        if (update.targetImageId != null && update.targetImage == null) {
          context.addIssue({
            code: 'custom',
            message: 'A resolved target image identity requires its configured reference.',
            path: ['update', 'targetImage'],
          });
        }
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

    if (profile.managerContractVersion >= 12) {
      if (profile.operationJournal == null) {
        context.addIssue({
          code: 'custom',
          message: 'Manager contract 12 requires a durable operation journal.',
          path: ['operationJournal'],
        });
      }
      if (profile.subsystemHealth == null) {
        context.addIssue({
          code: 'custom',
          message: 'Manager contract 12 requires Docker and GitHub subsystem health.',
          path: ['subsystemHealth'],
        });
      }
      if (profile.capacityEvidence == null) {
        context.addIssue({
          code: 'custom',
          message: 'Manager contract 12 requires capacity-deficit evidence.',
          path: ['capacityEvidence'],
        });
      }
    }

    const journal = profile.operationJournal;
    if (journal != null) {
      const sequences = new Set(journal.events.map((event) => event.sequence));
      if (sequences.size !== journal.events.length) {
        context.addIssue({
          code: 'custom',
          message: 'One durable sequence identifies exactly one manager event.',
          path: ['operationJournal', 'events'],
        });
      }
      if (journal.status === 'unavailable' && journal.events.length > 0) {
        context.addIssue({
          code: 'custom',
          message: 'An unavailable journal cannot carry retained events.',
          path: ['operationJournal', 'events'],
        });
      }
      if (journal.status === 'truncated' && journal.droppedEvents < 1) {
        context.addIssue({
          code: 'custom',
          message: 'A truncated journal must report the discarded entries.',
          path: ['operationJournal', 'droppedEvents'],
        });
      }
    }

    const capacityEvidence = profile.capacityEvidence;
    if (capacityEvidence != null) {
      if (profile.autoscaling == null && capacityEvidence.fixed == null) {
        context.addIssue({
          code: 'custom',
          message: 'A fixed-capacity profile reports fixed deficit evidence.',
          path: ['capacityEvidence', 'fixed'],
        });
      }
      if (profile.autoscaling != null && capacityEvidence.fixed != null) {
        context.addIssue({
          code: 'custom',
          message: 'An autoscaled profile reports per-target deficit evidence.',
          path: ['capacityEvidence', 'fixed'],
        });
      }
      [capacityEvidence.fixed, ...capacityEvidence.targets].forEach((deficit, index) => {
        if (deficit == null) return;
        if ((deficit.eligibleWorkers == null) !== (deficit.eligibilityDeficit == null)) {
          context.addIssue({
            code: 'custom',
            message: 'Eligible worker evidence and its deficit are available together.',
            path: ['capacityEvidence', index],
          });
        }
        if (deficit.localDeficit >= 1 && deficit.reason === 'none') {
          context.addIssue({
            code: 'custom',
            message: 'A reported shortfall carries a manager-supplied reason.',
            path: ['capacityEvidence', index, 'reason'],
          });
        }
      });
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

const recoveryCommandStatusSchema = z.enum([
  'queued',
  'claimed',
  'started',
  'succeeded',
  'rejected',
  'failed',
  'expired',
  'indeterminate',
]);

const recoveryCommandStateSchema = z.object({
  commandId: z.string().uuid(),
  status: recoveryCommandStatusSchema,
  failureCategory: z.string().nullable(),
  requestedByGitHubUserId: z.string(),
  requestedAt: offsetDateTimeSchema,
  expiresAt: offsetDateTimeSchema,
  deliveredAt: offsetDateTimeSchema.nullable(),
  claimedAt: offsetDateTimeSchema.nullable(),
  startedAt: offsetDateTimeSchema.nullable(),
  completedAt: offsetDateTimeSchema.nullable(),
  beforeManagerInstanceId: z.string().nullable(),
  afterManagerInstanceId: z.string().nullable(),
  resultMessage: z.string().nullable(),
});

const recoveryControlStateSchema = z.object({
  profileId: z.string(),
  managerContractVersion: z.number().int().nonnegative(),
  managerContractSupported: z.boolean(),
  expectedManagerInstanceId: z.string().nullable(),
  desiredGeneration: z.number().int().nonnegative(),
  desiredStateHash: z.string().nullable(),
  observedStateAgeSeconds: z.number().int().nonnegative(),
  observedStateMaximumAgeSeconds: z.number().int().positive(),
  recoveryAllowed: z.boolean(),
  singleManagerResolved: z.boolean(),
  operationActive: z.boolean(),
  latestCommand: recoveryCommandStateSchema.nullable(),
  recentCommands: z.array(recoveryCommandStateSchema).default([]),
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
  recoveryControls: z.array(recoveryControlStateSchema).default([]),
  hardware: hostHardwareInventorySchema.nullable().optional(),
});

export const operationalIncidentSchema = z.object({
  incidentId: z.string().uuid(),
  nodeId: z.string().uuid(),
  profileId: z.string().min(1).max(128).nullable(),
  kind: z.string().min(1).max(64),
  severity: z.enum(['warning', 'critical']),
  status: z.enum(['triggered', 'acknowledged', 'resolved']),
  title: z.string().min(1).max(160),
  summary: z.string().min(1).max(512),
  reason: z.string().min(1).max(128),
  evidence: z.string().max(512).nullable(),
  link: z.string().min(1).max(2048),
  firstObservedAt: offsetDateTimeSchema,
  triggeredAt: offsetDateTimeSchema,
  lastObservedAt: offsetDateTimeSchema,
  acknowledgedAt: offsetDateTimeSchema.nullable(),
  acknowledgedByGitHubUserId: z.string().min(1).nullable(),
  resolvedAt: offsetDateTimeSchema.nullable(),
});

const fleetResponseSchema = z.object({
  generatedAt: offsetDateTimeSchema,
  nodes: z.array(fleetNodeSchema),
  activeIncidents: z.array(operationalIncidentSchema).default([]),
});
const activeIncidentPageSchema = z.object({
  incidents: z.array(operationalIncidentSchema),
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
/** One durable manager contract 12 operation event. */
export type ManagerEvent = z.infer<typeof managerEventSchema>;
/** Bounded manager contract 12 durable operation journal. */
export type ManagerOperationJournal = z.infer<typeof managerOperationJournalSchema>;
/** One manager operation recorded as subsystem health evidence. */
export type SubsystemOperationEvidence = z.infer<typeof subsystemOperationEvidenceSchema>;
/** Manager-reported health of the operations performed against one subsystem. */
export type SubsystemHealthSummary = z.infer<typeof subsystemHealthSummarySchema>;
/** Manager contract 12 Docker and GitHub operation health. */
export type ManagerSubsystemHealth = z.infer<typeof managerSubsystemHealthSchema>;
/** Manager-reported evidence for missing capacity against one activation target. */
export type CapacityDeficitEvidence = z.infer<typeof capacityDeficitEvidenceSchema>;
/** Manager-reported capacity-deficit evidence for one autoscaling target. */
export type TargetCapacityDeficitEvidence = z.infer<typeof targetCapacityDeficitEvidenceSchema>;
/** Fixed or per-target manager contract 12 capacity-deficit evidence. */
export type ManagerCapacityEvidence = z.infer<typeof managerCapacityEvidenceSchema>;
/** Sanitized manager contract 13 node hardware inventory. */
export type HostHardwareInventory = z.infer<typeof hostHardwareInventorySchema>;
/** Credential-free projection published by one PitCrew manager. */
export type ManagerObservedState = z.infer<typeof managerObservedStateSchema>;
/** Lifecycle state of one connector capacity command. */
export type CapacityCommandState = z.infer<typeof capacityCommandStateSchema>;
/** Connector-advertised capacity control for one profile. */
export type CapacityControlState = z.infer<typeof capacityControlStateSchema>;
/** Immutable lifecycle and audit record of one manager-recovery command. */
export type RecoveryCommandState = z.infer<typeof recoveryCommandStateSchema>;
/** Lifecycle status of one manager-recovery command. */
export type RecoveryCommandStatus = z.infer<typeof recoveryCommandStatusSchema>;
/** Connector-advertised manager-recovery control for one profile. */
export type RecoveryControlState = z.infer<typeof recoveryControlStateSchema>;
/** One enrolled server and its latest profile projections. */
export type FleetNode = z.infer<typeof fleetNodeSchema>;
/** One durable operational incident. */
export type OperationalIncident = z.infer<typeof operationalIncidentSchema>;
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

/** Loads active incidents for shell-level severity navigation. */
export async function getActiveIncidents(
  tenantId: string,
  signal: AbortSignal,
): Promise<ReadonlyArray<OperationalIncident>> {
  const page = await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/incidents?status=active`,
    {
      method: 'GET',
      schema: activeIncidentPageSchema,
      signal,
    },
  );
  return page.incidents;
}
