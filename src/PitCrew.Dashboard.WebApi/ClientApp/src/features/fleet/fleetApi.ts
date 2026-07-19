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
  isRevoked: z.boolean(),
  credentialRotationRequested: z.boolean(),
  profiles: z.array(managerObservedStateSchema),
});

const fleetResponseSchema = z.object({
  generatedAt: offsetDateTimeSchema,
  nodes: z.array(fleetNodeSchema),
});

/** Credential-free lifecycle state for one manager slot. */
export type ObservedSlot = z.infer<typeof observedSlotSchema>;
/** Credential-free projection published by one PitCrew manager. */
export type ManagerObservedState = z.infer<typeof managerObservedStateSchema>;
/** One enrolled server and its latest profile projections. */
export type FleetNode = z.infer<typeof fleetNodeSchema>;
/** Current tenant fleet response. */
export type FleetResponse = z.infer<typeof fleetResponseSchema>;

const enrollmentCodeResponseSchema = z.object({
  enrollmentCodeId: z.string().uuid(),
  code: z.string(),
  expiresAt: offsetDateTimeSchema,
});

/** One-time connector enrollment code returned only at creation. */
export type EnrollmentCodeResponse = z.infer<typeof enrollmentCodeResponseSchema>;

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

/** Creates one expiring enrollment code that is returned only once. */
export async function createEnrollmentCode(
  tenantId: string,
  label: string,
  antiforgeryToken: string,
): Promise<EnrollmentCodeResponse> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/enrollment-codes`,
    {
      method: 'POST',
      body: { label },
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
      schema: enrollmentCodeResponseSchema,
    },
  );
}

/** Revokes a node credential immediately. */
export async function revokeNode(
  tenantId: string,
  nodeId: string,
  antiforgeryToken: string,
): Promise<void> {
  await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/nodes/${encodeURIComponent(nodeId)}/revoke`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    },
  );
}

/** Changes the operator-facing name of one enrolled server. */
export async function renameNode(
  tenantId: string,
  nodeId: string,
  displayName: string,
  antiforgeryToken: string,
): Promise<void> {
  await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/nodes/${encodeURIComponent(nodeId)}`,
    {
      method: 'PUT',
      body: { displayName },
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    },
  );
}

/** Requests connector-delivered credential rotation on the next protocol-v2 sync. */
export async function requestCredentialRotation(
  tenantId: string,
  nodeId: string,
  antiforgeryToken: string,
): Promise<void> {
  await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/nodes/${encodeURIComponent(nodeId)}/credential-rotation`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    },
  );
}
