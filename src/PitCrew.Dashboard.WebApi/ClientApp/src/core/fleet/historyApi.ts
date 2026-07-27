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
  updatedAt: offsetDateTimeSchema.nullable(),
});

const profileHistorySchema = z.object({
  profileId: z.string().min(1).max(128),
  samples: z.array(telemetrySampleSchema),
  rollups: z.array(telemetryRollupSchema),
  events: z.array(managerEventSchema),
  pointsTruncated: z.boolean(),
  eventsTruncated: z.boolean(),
  journal: eventJournalStateSchema,
});

const nodeHistorySchema = z.object({
  nodeId: z.string().uuid(),
  generatedAt: offsetDateTimeSchema,
  from: offsetDateTimeSchema,
  to: offsetDateTimeSchema,
  resolution: historyResolutionSchema,
  profiles: z.array(profileHistorySchema),
});

/** Stored resolution served by one bounded history query. */
export type HistoryResolution = z.infer<typeof historyResolutionSchema>;
/** One retained profile observation with unavailable and measured-zero distinctions preserved. */
export type ProfileTelemetrySample = z.infer<typeof telemetrySampleSchema>;
/** One deterministic hourly rollup derived from retained samples. */
export type ProfileTelemetryRollup = z.infer<typeof telemetryRollupSchema>;
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
