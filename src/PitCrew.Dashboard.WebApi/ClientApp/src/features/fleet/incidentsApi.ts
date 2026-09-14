import { z } from 'zod';

import { ApiError, HttpClient } from '@/core/api/httpClient';
import { operationalIncidentSchema, type OperationalIncident } from '@/core/fleet';

const offsetDateTimeSchema = z.string().datetime({ offset: true });
export const incidentPageSchema = z.object({
  generatedAt: offsetDateTimeSchema,
  incidents: z.array(operationalIncidentSchema),
  truncated: z.boolean(),
  totalCount: z.number().int().nonnegative().optional(),
  criticalCount: z.number().int().nonnegative().optional(),
  warningCount: z.number().int().nonnegative().optional(),
  nextCursor: z.string().min(1).nullable().optional(),
});
const incidentDetailSchema = z.object({
  generatedAt: offsetDateTimeSchema,
  incident: operationalIncidentSchema,
});

/** Visible lifecycle filter accepted by the incident API. */
export type IncidentFilter = 'active' | 'resolved' | 'all';

export type { OperationalIncident };

/** Bounded incident page returned by the dashboard. */
export type IncidentPage = z.infer<typeof incidentPageSchema>;

function createClient(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
}

/** Loads bounded visible incidents for one tenant. */
export async function getIncidents(
  tenantId: string,
  status: IncidentFilter,
  signal?: AbortSignal,
  cursor?: string,
): Promise<IncidentPage> {
  const query = new URLSearchParams({ status });
  if (cursor) query.set('cursor', cursor);
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/incidents?${query.toString()}`,
    {
      signal,
      schema: incidentPageSchema,
    },
  );
}

/** Loads one exact authorized incident, including records outside the first page. */
export async function getIncidentOrNull(
  tenantId: string,
  incidentId: string,
  signal?: AbortSignal,
): Promise<OperationalIncident | null> {
  try {
    const detail = await createClient().request(
      `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/incidents/${encodeURIComponent(incidentId)}`,
      {
        signal,
        schema: incidentDetailSchema,
      },
    );
    return detail.incident;
  } catch (caught) {
    if (caught instanceof ApiError && caught.status === 404) return null;
    throw caught;
  }
}

/** Acknowledges one active incident without resolving it. */
export async function acknowledgeIncident(
  tenantId: string,
  incidentId: string,
  antiforgeryToken: string,
  signal?: AbortSignal,
): Promise<OperationalIncident> {
  await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/incidents/${encodeURIComponent(incidentId)}/acknowledge`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
      signal,
    },
  );
  return await getRequiredIncident(tenantId, incidentId, signal);
}

/** Reverses acknowledgement, returning the incident to the triggered state. */
export async function unacknowledgeIncident(
  tenantId: string,
  incidentId: string,
  antiforgeryToken: string,
  signal?: AbortSignal,
): Promise<OperationalIncident> {
  await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/incidents/${encodeURIComponent(incidentId)}/unacknowledge`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
      signal,
    },
  );
  return await getRequiredIncident(tenantId, incidentId, signal);
}

async function getRequiredIncident(
  tenantId: string,
  incidentId: string,
  signal?: AbortSignal,
): Promise<OperationalIncident> {
  const incident = await getIncidentOrNull(tenantId, incidentId, signal);
  if (incident == null) {
    throw new Error('The updated incident could not be retrieved.');
  }
  return incident;
}
