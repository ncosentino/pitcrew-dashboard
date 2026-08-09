import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

import {
  currentJobSchema,
  hostHardwareInventorySchema,
  managerEventSchema,
  offsetDateTimeSchema,
} from './fleetApi';

const historyResolutionSchema = z.enum(['raw', 'hourly']);

const telemetrySampleSchema = z.object({
  observedAt: offsetDateTimeSchema,
  sampledAt: offsetDateTimeSchema.nullable(),
  telemetryStatus: z.string().min(1).max(32),
  managerInstanceId: z.string().min(1).max(128),
  managerStatus: z.string().min(1).max(32),
  generation: z.number().int().nonnegative(),
  desiredSlots: z.number().int().nonnegative(),
  activeSlots: z.number().int().nonnegative(),
  drainingSlots: z.number().int().nonnegative(),
  configuredSlots: z.number().int().nonnegative().nullable(),
  eligibleSlots: z.number().int().nonnegative().nullable(),
  targetSlots: z.number().int().nonnegative().nullable(),
  maximumSlots: z.number().int().nonnegative().nullable(),
  assignedJobs: z.number().int().nonnegative().nullable(),
  runningJobs: z.number().int().nonnegative().nullable(),
  availableJobs: z.number().int().nonnegative().nullable(),
  idleRunners: z.number().int().nonnegative().nullable(),
  busyRunners: z.number().int().nonnegative().nullable(),
  localRunningWorkers: z.number().int().nonnegative(),
  managerCpuCores: z.number().nonnegative().nullable(),
  managerMemoryBytes: z.number().int().nonnegative().nullable(),
  managerPids: z.number().int().nonnegative().nullable(),
  hostLogicalProcessorCount: z.number().int().nonnegative().nullable(),
  hostMemoryBytes: z.number().int().nonnegative().nullable(),
  hostPressureStatus: z.enum(['available', 'partial', 'unavailable']).nullable().default(null),
  hostCpuUtilizationPercent: z.number().min(0).max(100).nullable().default(null),
  hostLoad1: z.number().nonnegative().nullable().default(null),
  hostLoad5: z.number().nonnegative().nullable().default(null),
  hostLoad15: z.number().nonnegative().nullable().default(null),
  hostPressureMemoryTotalBytes: z.number().int().positive().nullable().default(null),
  hostMemoryAvailableBytes: z.number().int().nonnegative().nullable().default(null),
  hostSwapUsedBytes: z.number().int().nonnegative().nullable().default(null),
  hostCpuPressureSomeAvg10: z.number().min(0).max(100).nullable().default(null),
  hostCpuPressureFullAvg10: z.number().min(0).max(100).nullable().default(null),
  hostMemoryPressureSomeAvg10: z.number().min(0).max(100).nullable().default(null),
  hostMemoryPressureFullAvg10: z.number().min(0).max(100).nullable().default(null),
  hostIoPressureSomeAvg10: z.number().min(0).max(100).nullable().default(null),
  hostIoPressureFullAvg10: z.number().min(0).max(100).nullable().default(null),
  hostAdmissionStatus: z
    .enum(['disabled', 'available', 'degraded', 'unavailable'])
    .nullable()
    .default(null),
  hostAdmissionNamespace: z
    .string()
    .min(1)
    .max(32)
    .regex(/^[a-z][a-z0-9-]*$/u)
    .nullable()
    .default(null),
  hostAdmissionEpoch: z.number().int().nonnegative().nullable().default(null),
  hostAdmissionDecisionSequence: z.number().int().nonnegative().nullable().default(null),
  hostAdmissionCapacityUnits: z.number().int().positive().nullable().default(null),
  hostAdmissionSafetyMarginUnits: z.number().int().nonnegative().nullable().default(null),
  hostAdmissionEffectiveTotalUnits: z.number().int().positive().nullable().default(null),
  hostAdmissionAvailableUnits: z.number().int().nonnegative().nullable().default(null),
  hostAdmissionUnitCost: z.number().int().positive().nullable().default(null),
  hostAdmissionReservedUnits: z.number().int().nonnegative().nullable().default(null),
  hostAdmissionBorrowable: z.boolean().nullable().default(null),
  hostAdmissionActiveUnits: z.number().int().nonnegative().nullable().default(null),
  hostAdmissionProvisionalUnits: z.number().int().nonnegative().nullable().default(null),
  hostAdmissionHeldUnits: z.number().int().nonnegative().nullable().default(null),
  hostAdmissionBorrowedUnits: z.number().int().nonnegative().nullable().default(null),
  hostAdmissionPendingUnits: z.number().int().nonnegative().nullable().default(null),
  hostAdmissionWithheldUnits: z.number().int().nonnegative().nullable().default(null),
  workerCpuCores: z.number().nonnegative().nullable(),
  workerMemoryBytes: z.number().int().nonnegative().nullable(),
  workerPids: z.number().int().nonnegative().nullable(),
  networkRxBytes: z.number().int().nonnegative().nullable(),
  networkTxBytes: z.number().int().nonnegative().nullable(),
  blockReadBytes: z.number().int().nonnegative().nullable(),
  blockWriteBytes: z.number().int().nonnegative().nullable(),
  exitReports: z.number().int().nonnegative(),
  adverseExitReports: z.number().int().nonnegative(),
  localCapacityDeficit: z.number().int().nonnegative().nullable(),
  eligibilityCapacityDeficit: z.number().int().nonnegative().nullable(),
  capacityDeficitReason: z.string().min(1).max(128).nullable(),
  capacityDeficitFreshness: z.string().min(1).max(32).nullable(),
  workerUpdateStatus: z.enum(['current', 'rolling', 'degraded']).nullable().default(null),
  workerTargetImage: z.string().min(1).max(2048).nullable().default(null),
  workerTargetImageId: z
    .string()
    .regex(/^sha256:[0-9a-f]{64}$/u)
    .nullable()
    .default(null),
  workerTargetRevision: z
    .string()
    .regex(/^[0-9a-f]{64}$/u)
    .nullable()
    .default(null),
  workerCurrentWorkers: z.number().int().nonnegative().nullable().default(null),
  workerStaleWorkers: z.number().int().nonnegative().nullable().default(null),
  workerUpdateError: z.string().max(512).nullable().default(null),
});

const telemetryRollupSchema = z.object({
  bucketStart: offsetDateTimeSchema,
  sampleCount: z.number().int().nonnegative(),
  maximumDesiredSlots: z.number().int().nonnegative(),
  maximumActiveSlots: z.number().int().nonnegative(),
  maximumDrainingSlots: z.number().int().nonnegative(),
  maximumEligibleSlots: z.number().int().nonnegative().nullable(),
  maximumLocalRunningWorkers: z.number().int().nonnegative(),
  maximumManagerCpuCores: z.number().nonnegative().nullable(),
  maximumManagerMemoryBytes: z.number().int().nonnegative().nullable(),
  maximumManagerPids: z.number().int().nonnegative().nullable(),
  maximumWorkerCpuCores: z.number().nonnegative().nullable(),
  maximumWorkerMemoryBytes: z.number().int().nonnegative().nullable(),
  maximumWorkerPids: z.number().int().nonnegative().nullable(),
  maximumNetworkRxBytes: z.number().int().nonnegative().nullable(),
  maximumNetworkTxBytes: z.number().int().nonnegative().nullable(),
  maximumBlockReadBytes: z.number().int().nonnegative().nullable(),
  maximumBlockWriteBytes: z.number().int().nonnegative().nullable(),
  maximumExitReports: z.number().int().nonnegative(),
  maximumAdverseExitReports: z.number().int().nonnegative(),
  maximumLocalCapacityDeficit: z.number().int().nonnegative().nullable(),
  maximumEligibilityCapacityDeficit: z.number().int().nonnegative().nullable(),
  maximumTargetSlots: z.number().int().nonnegative().nullable(),
  maximumAssignedJobs: z.number().int().nonnegative().nullable(),
  maximumIdleRunners: z.number().int().nonnegative().nullable(),
  maximumBusyRunners: z.number().int().nonnegative().nullable(),
  maximumHostCpuUtilizationPercent: z.number().min(0).max(100).nullable().default(null),
  maximumHostLoad1: z.number().nonnegative().nullable().default(null),
  maximumHostLoad5: z.number().nonnegative().nullable().default(null),
  maximumHostLoad15: z.number().nonnegative().nullable().default(null),
  minimumHostMemoryAvailableBytes: z.number().int().nonnegative().nullable().default(null),
  maximumHostSwapUsedBytes: z.number().int().nonnegative().nullable().default(null),
  maximumHostCpuPressureSomeAvg10: z.number().min(0).max(100).nullable().default(null),
  maximumHostCpuPressureFullAvg10: z.number().min(0).max(100).nullable().default(null),
  maximumHostMemoryPressureSomeAvg10: z.number().min(0).max(100).nullable().default(null),
  maximumHostMemoryPressureFullAvg10: z.number().min(0).max(100).nullable().default(null),
  maximumHostIoPressureSomeAvg10: z.number().min(0).max(100).nullable().default(null),
  maximumHostIoPressureFullAvg10: z.number().min(0).max(100).nullable().default(null),
});

const eventJournalStateSchema = z.object({
  status: z.string().min(1).max(32),
  capacity: z.number().int().nonnegative(),
  managerHighestSequence: z.number().int().nonnegative().nullable(),
  storedLowestSequence: z.number().int().nonnegative().nullable(),
  storedHighestSequence: z.number().int().nonnegative().nullable(),
  managerDroppedEvents: z.number().int().nonnegative(),
  missedEvents: z.number().int().nonnegative(),
  undeliveredEvents: z.number().int().nonnegative(),
  epoch: z.number().int().nonnegative(),
  epochResets: z.number().int().nonnegative(),
  rejectedFutureEvents: z.number().int().nonnegative(),
  updatedAt: offsetDateTimeSchema.nullable(),
});

const retentionFloorSchema = z.object({
  earliestRetainedSample: offsetDateTimeSchema.nullable(),
  droppedSamples: z.number().int().nonnegative(),
  earliestRetainedRollup: offsetDateTimeSchema.nullable(),
  droppedRollups: z.number().int().nonnegative(),
  earliestRetainedEvent: offsetDateTimeSchema.nullable(),
  droppedEvents: z.number().int().nonnegative(),
  earliestRetainedSubsystemHealthChange: offsetDateTimeSchema.nullable(),
  droppedSubsystemHealthChanges: z.number().int().nonnegative(),
  earliestRetainedCapacityDeficit: offsetDateTimeSchema.nullable(),
  droppedCapacityDeficits: z.number().int().nonnegative(),
  earliestRetainedRunnerAssignment: offsetDateTimeSchema.nullable().default(null),
  droppedRunnerAssignments: z.number().int().nonnegative().default(0),
  rejectedFutureSamples: z.number().int().nonnegative(),
  historyExpiredAt: offsetDateTimeSchema.nullable(),
});

const subsystemHealthChangeSchema = z.object({
  subsystem: z.string().min(1).max(32),
  observedAt: offsetDateTimeSchema,
  state: z.string().min(1).max(32),
  consecutiveFailures: z.number().int().nonnegative(),
  retryAt: offsetDateTimeSchema.nullable(),
  lastSuccessOperation: z.string().min(1).max(64).nullable(),
  lastSuccessObservedAt: offsetDateTimeSchema.nullable(),
  lastSuccessReason: z.string().min(1).max(128).nullable(),
  lastFailureOperation: z.string().min(1).max(64).nullable(),
  lastFailureObservedAt: offsetDateTimeSchema.nullable(),
  lastFailureReason: z.string().min(1).max(128).nullable(),
  lastFailureEvidence: z.string().min(1).max(512).nullable(),
});

const capacityDeficitObservationSchema = z.object({
  targetKey: z.string().min(1).max(128),
  observedAt: offsetDateTimeSchema,
  repository: z.string().min(1).max(2048).nullable(),
  freshness: z.string().min(1).max(32),
  targetSlots: z.number().int(),
  activeWorkers: z.number().int(),
  startingWorkers: z.number().int(),
  drainingWorkers: z.number().int(),
  cleanupPendingWorkers: z.number().int(),
  eligibleWorkers: z.number().int().nullable(),
  localDeficit: z.number().int(),
  eligibilityDeficit: z.number().int().nullable(),
  reason: z.string().min(1).max(128),
  evidence: z.string().min(1).max(512).nullable(),
});

const workerUpdateChangeSchema = z.object({
  kind: z.enum(['target-changed', 'rollout-started', 'rollout-converged', 'rollout-degraded']),
  observedAt: offsetDateTimeSchema,
  status: z.enum(['current', 'rolling', 'degraded']),
  targetImage: z.string().min(1).max(2048).nullable(),
  targetImageId: z
    .string()
    .regex(/^sha256:[0-9a-f]{64}$/u)
    .nullable(),
  targetRevision: z.string().regex(/^[0-9a-f]{64}$/u),
  currentWorkers: z.number().int().nonnegative(),
  staleWorkers: z.number().int().nonnegative(),
  lastError: z.string().max(512).nullable(),
});

const profileHistorySchema = z.object({
  profileId: z.string().min(1).max(128),
  samples: z.array(telemetrySampleSchema),
  rollups: z.array(telemetryRollupSchema),
  events: z.array(managerEventSchema),
  subsystemHealthChanges: z.array(subsystemHealthChangeSchema),
  capacityDeficits: z.array(capacityDeficitObservationSchema),
  workerUpdateChanges: z.array(workerUpdateChangeSchema).default([]),
  pointsTruncated: z.boolean(),
  eventsTruncated: z.boolean(),
  subsystemHealthTruncated: z.boolean(),
  capacityDeficitsTruncated: z.boolean(),
  workerUpdatesTruncated: z.boolean().default(false),
  journal: eventJournalStateSchema,
  retention: retentionFloorSchema,
});

const incompletenessFloorSchema = z.object({
  scope: z.string().min(1).max(16),
  earliestExpiredAt: offsetDateTimeSchema,
  latestExpiredAt: offsetDateTimeSchema,
  expiredProfiles: z.number().int().nonnegative(),
  droppedSamples: z.number().int().nonnegative(),
  droppedRollups: z.number().int().nonnegative(),
  droppedEvents: z.number().int().nonnegative(),
  droppedSubsystemHealthChanges: z.number().int().nonnegative(),
  droppedCapacityDeficits: z.number().int().nonnegative(),
  droppedHardwareRevisions: z.number().int().nonnegative().default(0),
  droppedRunnerAssignments: z.number().int().nonnegative().default(0),
});

const hostHardwareRevisionSchema = z.object({
  inventoryHash: z.string().regex(/^[0-9a-f]{64}$/u),
  collectedAt: offsetDateTimeSchema,
  firstObservedAt: offsetDateTimeSchema,
  lastObservedAt: offsetDateTimeSchema,
  sourceProfileId: z.string().min(1).max(32),
  hardware: hostHardwareInventorySchema,
});

const runnerAssignmentIntervalSchema = z.object({
  runnerNameHash: z.string().regex(/^[0-9a-f]{64}$/u),
  profileId: z.string().min(1).max(32),
  slotKey: z.string().min(1).max(128),
  repository: z.string().min(1).max(2048).nullable(),
  target: z.string().min(1).max(512).nullable(),
  job: currentJobSchema.nullable().default(null),
  firstObservedAt: offsetDateTimeSchema,
  lastObservedAt: offsetDateTimeSchema,
});

const nodeHistorySchema = z.object({
  nodeId: z.string().uuid(),
  generatedAt: offsetDateTimeSchema,
  from: offsetDateTimeSchema,
  to: offsetDateTimeSchema,
  resolution: historyResolutionSchema,
  profiles: z.array(profileHistorySchema),
  pointsTruncated: z.boolean(),
  eventsTruncated: z.boolean(),
  diagnosticsTruncated: z.boolean(),
  profilePointLimit: z.number().int().positive(),
  profileEventLimit: z.number().int().positive(),
  profileSubsystemHealthLimit: z.number().int().positive(),
  profileCapacityDeficitLimit: z.number().int().positive(),
  profileWorkerUpdateLimit: z.number().int().nonnegative().default(0),
  nodePointLimit: z.number().int().positive(),
  nodeEventLimit: z.number().int().positive(),
  nodeDiagnosticLimit: z.number().int().positive(),
  incompletenessFloors: z.array(incompletenessFloorSchema),
  hardwareRevisions: z.array(hostHardwareRevisionSchema).default([]),
  hardwareRevisionsTruncated: z.boolean().default(false),
  runnerAssignments: z.array(runnerAssignmentIntervalSchema).default([]),
  runnerAssignmentsTruncated: z.boolean().default(false),
});

const historyCapabilitiesSchema = z.object({
  defaultRangeHours: z.number().int().positive(),
  maximumRangeHours: z.number().int().positive(),
  resolutions: z.array(historyResolutionSchema).min(1),
  maximumPoints: z.number().int().positive(),
  maximumEvents: z.number().int().positive(),
  maximumDiagnostics: z.number().int().positive(),
  nodePointLimit: z.number().int().positive(),
  nodeEventLimit: z.number().int().positive(),
  nodeDiagnosticLimit: z.number().int().positive(),
  expectedRawCadenceSeconds: z.number().int().positive(),
  sampleRetentionHours: z.number().int().positive(),
  rollupRetentionHours: z.number().int().positive(),
});

/** Server-advertised history query limits used to build only requests the server accepts. */
export type HistoryCapabilities = z.infer<typeof historyCapabilitiesSchema>;

/** Stored resolution served by one bounded history query. */
export type HistoryResolution = z.infer<typeof historyResolutionSchema>;
/** One retained profile observation with unavailable and measured-zero distinctions preserved. */
export type ProfileTelemetrySample = z.infer<typeof telemetrySampleSchema>;
/** One deterministic hourly rollup derived from retained samples. */
export type ProfileTelemetryRollup = z.infer<typeof telemetryRollupSchema>;
/** Retention floor and dropped counters for one retained profile. */
export type ProfileRetentionFloor = z.infer<typeof retentionFloorSchema>;
/** One retained contract-12 subsystem health change. */
export type ProfileSubsystemHealthChange = z.infer<typeof subsystemHealthChangeSchema>;
/** One retained target-keyed capacity-deficit observation. */
export type ProfileCapacityDeficitObservation = z.infer<typeof capacityDeficitObservationSchema>;
/** One worker-image rollout transition derived from retained profile samples. */
export type ProfileWorkerUpdateChange = z.infer<typeof workerUpdateChangeSchema>;
/** Explicit durable manager-journal availability and gap state. */
export type ProfileEventJournalState = z.infer<typeof eventJournalStateSchema>;
/** Bounded retained history for one profile. */
export type ProfileHistory = z.infer<typeof profileHistorySchema>;
/** Coarse node or database record of history whose per-profile provenance was compacted away. */
export type HistoryIncompletenessFloor = z.infer<typeof incompletenessFloorSchema>;
/** One deduplicated node hardware change in retained history. */
export type HostHardwareRevision = z.infer<typeof hostHardwareRevisionSchema>;
/** One retained exact runner-to-profile assignment interval. */
export type RunnerAssignmentInterval = z.infer<typeof runnerAssignmentIntervalSchema>;
/** Bounded retained history for one tenant node. */
export type NodeHistoryResponse = z.infer<typeof nodeHistorySchema>;

/** Bounded time range and point limits requested from the history API. */
export interface HistoryQuery {
  readonly from?: string;
  readonly to?: string;
  readonly resolution?: HistoryResolution;
  readonly points?: number;
  readonly events?: number;
  readonly diagnostics?: number;
}

function buildQueryString(query: HistoryQuery): string {
  const parameters = new URLSearchParams();
  if (query.from != null) {
    parameters.set('from', query.from);
  }
  if (query.to != null) {
    parameters.set('to', query.to);
  }
  if (query.resolution != null) {
    parameters.set('resolution', query.resolution);
  }
  if (query.points != null) {
    parameters.set('points', String(query.points));
  }
  if (query.events != null) {
    parameters.set('events', String(query.events));
  }
  if (query.diagnostics != null) {
    parameters.set('diagnostics', String(query.diagnostics));
  }
  const serialized = parameters.toString();
  return serialized.length === 0 ? '' : `?${serialized}`;
}

function createClient(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
}

/** Loads the server-advertised history capabilities for one authorized tenant. */
export async function getHistoryCapabilities(
  tenantId: string,
  signal: AbortSignal,
): Promise<HistoryCapabilities> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/history/capabilities`,
    { method: 'GET', schema: historyCapabilitiesSchema, signal },
  );
}

/** Loads bounded retained history for every profile of one authorized tenant node. */
export async function getNodeHistory(
  tenantId: string,
  nodeId: string,
  query: HistoryQuery,
  signal: AbortSignal,
): Promise<NodeHistoryResponse> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/nodes/${encodeURIComponent(nodeId)}/history${buildQueryString(query)}`,
    { method: 'GET', schema: nodeHistorySchema, signal },
  );
}

/** Loads bounded retained history for one profile of one authorized tenant node. */
export async function getProfileHistory(
  tenantId: string,
  nodeId: string,
  profileId: string,
  query: HistoryQuery,
  signal: AbortSignal,
): Promise<NodeHistoryResponse> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/nodes/${encodeURIComponent(nodeId)}/profiles/${encodeURIComponent(profileId)}/history${buildQueryString(query)}`,
    { method: 'GET', schema: nodeHistorySchema, signal },
  );
}
