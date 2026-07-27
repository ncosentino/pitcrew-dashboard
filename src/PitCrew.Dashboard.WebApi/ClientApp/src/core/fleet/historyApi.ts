import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

import { managerEventSchema, offsetDateTimeSchema } from './fleetApi';

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
  rejectedFutureSamples: z.number().int().nonnegative(),
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

const profileHistorySchema = z.object({
  profileId: z.string().min(1).max(128),
  samples: z.array(telemetrySampleSchema),
  rollups: z.array(telemetryRollupSchema),
  events: z.array(managerEventSchema),
  subsystemHealthChanges: z.array(subsystemHealthChangeSchema),
  capacityDeficits: z.array(capacityDeficitObservationSchema),
  pointsTruncated: z.boolean(),
  eventsTruncated: z.boolean(),
  subsystemHealthTruncated: z.boolean(),
  capacityDeficitsTruncated: z.boolean(),
  journal: eventJournalStateSchema,
  retention: retentionFloorSchema,
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
  profileDiagnosticLimit: z.number().int().positive(),
  nodePointLimit: z.number().int().positive(),
  nodeEventLimit: z.number().int().positive(),
  nodeDiagnosticLimit: z.number().int().positive(),
});

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
/** Explicit durable manager-journal availability and gap state. */
export type ProfileEventJournalState = z.infer<typeof eventJournalStateSchema>;
/** Bounded retained history for one profile. */
export type ProfileHistory = z.infer<typeof profileHistorySchema>;
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
