import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const offsetDateTimeSchema = z.string().datetime({ offset: true });
const incidentSchema = z.object({
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
const incidentPageSchema = z.object({
  generatedAt: offsetDateTimeSchema,
  incidents: z.array(incidentSchema),
  truncated: z.boolean(),
});

/** Visible lifecycle filter accepted by the incident API. */
export type IncidentFilter = 'active' | 'resolved' | 'all';

/** One durable operational incident. */
export type OperationalIncident = z.infer<typeof incidentSchema>;

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
