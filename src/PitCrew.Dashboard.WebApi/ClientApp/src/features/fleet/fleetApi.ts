import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const setCapacityMaximumResponseSchema = z.object({
  commandId: z.string().uuid(),
  status: z.literal('pending'),
});

/** Queued capacity command returned by the dashboard. */
export type SetCapacityMaximumResponse = z.infer<typeof setCapacityMaximumResponseSchema>;

function createClient(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
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

/** Queues an absolute maximum for one connector-advertised profile target. */
export async function setCapacityMaximum(
  tenantId: string,
  nodeId: string,
  profileId: string,
  maximum: number,
  antiforgeryToken: string,
): Promise<SetCapacityMaximumResponse> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/nodes/${encodeURIComponent(nodeId)}/profiles/${encodeURIComponent(profileId)}/capacity-maximum`,
    {
      method: 'POST',
      body: { maximum },
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
      schema: setCapacityMaximumResponseSchema,
    },
  );
}
