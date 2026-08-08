import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

export const tenantRoleSchema = z.enum(['viewer', 'administrator', 'owner']);

export const dashboardUserSchema = z.object({
  githubUserId: z.string(),
  githubLogin: z.string(),
  displayName: z.string(),
  avatarUrl: z.string().nullable(),
});

export const tenantAccessSchema = z.object({
  tenantId: z.string(),
  displayName: z.string(),
  role: tenantRoleSchema,
});

export const dashboardSessionSchema = z.object({
  user: dashboardUserSchema,
  isSystemAdministrator: z.boolean(),
  tenants: z.array(tenantAccessSchema),
  antiforgeryToken: z.string(),
});

/** Authenticated GitHub identity returned by the dashboard. */
export type DashboardUser = z.infer<typeof dashboardUserSchema>;
/** Tenant authorization role returned by the dashboard. */
export type TenantRole = z.infer<typeof tenantRoleSchema>;
/** Tenant context available to the authenticated user. */
export type TenantAccess = z.infer<typeof tenantAccessSchema>;
/** Authenticated session, tenant contexts, and antiforgery token. */
export type DashboardSession = z.infer<typeof dashboardSessionSchema>;

function createClient(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
}

/** Loads the authenticated user, available tenants, and antiforgery token. */
export async function getSession(signal: AbortSignal): Promise<DashboardSession> {
  return await createClient().request('/api/session', {
    method: 'GET',
    schema: dashboardSessionSchema,
    signal,
  });
}

/** Ends the authenticated dashboard cookie session. */
export async function logout(antiforgeryToken: string): Promise<void> {
  await createClient().request('/auth/logout', {
    method: 'POST',
    headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
  });
}
