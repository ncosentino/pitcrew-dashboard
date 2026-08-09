import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';
import { operationalIncidentSchema, type OperationalIncident } from '@/core/fleet';

const offsetDateTimeSchema = z.string().datetime({ offset: true });
export const incidentPageSchema = z.object({
  generatedAt: offsetDateTimeSchema,
  incidents: z.array(operationalIncidentSchema),
  truncated: z.boolean(),
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
): Promise<IncidentPage> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/incidents?status=${status}`,
    {
      signal,
      schema: incidentPageSchema,
    },
  );
}

/** Acknowledges one active incident without resolving it. */
export async function acknowledgeIncident(
  tenantId: string,
  incidentId: string,
  antiforgeryToken: string,
): Promise<void> {
  await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/incidents/${encodeURIComponent(incidentId)}/acknowledge`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    },
  );
}

/** Reverses acknowledgement, returning the incident to the triggered state. */
export async function unacknowledgeIncident(
  tenantId: string,
  incidentId: string,
  antiforgeryToken: string,
): Promise<void> {
  await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/fleet/v1/incidents/${encodeURIComponent(incidentId)}/unacknowledge`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    },
  );
}
