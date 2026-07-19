import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const offsetDateTimeSchema = z.string().datetime({ offset: true });
const tenantRoleSchema = z.enum(['viewer', 'administrator', 'owner']);

const dashboardUserSchema = z.object({
  githubUserId: z.string(),
  githubLogin: z.string(),
  displayName: z.string(),
  avatarUrl: z.string().nullable(),
});

const tenantAccessSchema = z.object({
  tenantId: z.string(),
  displayName: z.string(),
  role: tenantRoleSchema,
});

const dashboardSessionSchema = z.object({
  user: dashboardUserSchema,
  isSystemAdministrator: z.boolean(),
  tenants: z.array(tenantAccessSchema),
  antiforgeryToken: z.string(),
});

const tenantMemberSchema = z.object({
  user: dashboardUserSchema,
  role: tenantRoleSchema,
  createdAt: offsetDateTimeSchema,
});

const tenantMembersSchema = z.array(tenantMemberSchema);
const dashboardUsersSchema = z.array(dashboardUserSchema);

/** Authenticated GitHub identity returned by the dashboard. */
export type DashboardUser = z.infer<typeof dashboardUserSchema>;
/** Tenant authorization role returned by the dashboard. */
export type TenantRole = z.infer<typeof tenantRoleSchema>;
/** Tenant context available to the authenticated user. */
export type TenantAccess = z.infer<typeof tenantAccessSchema>;
/** Authenticated session, tenant contexts, and antiforgery token. */
export type DashboardSession = z.infer<typeof dashboardSessionSchema>;
/** Persisted tenant membership returned to an owner. */
export type TenantMember = z.infer<typeof tenantMemberSchema>;

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

/** Creates one tenant and grants ownership to the current system administrator. */
export async function createTenant(
  tenantId: string,
  displayName: string,
  antiforgeryToken: string,
): Promise<void> {
  await createClient().request('/api/tenants', {
    method: 'POST',
    body: { tenantId, displayName },
    headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
  });
}

/** Changes the operator-facing name of one tenant without changing its stable ID. */
export async function renameTenant(
  tenantId: string,
  displayName: string,
  antiforgeryToken: string,
): Promise<void> {
  await createClient().request(`/api/tenants/${encodeURIComponent(tenantId)}`, {
    method: 'PUT',
    body: { displayName },
    headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
  });
}

/** Loads all memberships for one tenant. */
export async function getTenantMembers(
  tenantId: string,
  signal: AbortSignal,
): Promise<readonly TenantMember[]> {
  return await createClient().request(`/api/tenants/${encodeURIComponent(tenantId)}/members`, {
    method: 'GET',
    schema: tenantMembersSchema,
    signal,
  });
}

/** Loads authenticated users that can be added to one tenant. */
export async function getAvailableUsers(
  tenantId: string,
  signal: AbortSignal,
): Promise<readonly DashboardUser[]> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/available-users`,
    {
      method: 'GET',
      schema: dashboardUsersSchema,
      signal,
    },
  );
}

/** Creates or updates one tenant membership. */
export async function setTenantMembership(
  tenantId: string,
  githubUserId: string,
  role: TenantRole,
  antiforgeryToken: string,
): Promise<void> {
  await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/members/${encodeURIComponent(githubUserId)}`,
    {
      method: 'PUT',
      body: { role },
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    },
  );
}

/** Removes one tenant membership while preserving the final-owner invariant. */
export async function removeTenantMembership(
  tenantId: string,
  githubUserId: string,
  antiforgeryToken: string,
): Promise<void> {
  await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/members/${encodeURIComponent(githubUserId)}`,
    {
      method: 'DELETE',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    },
  );
}

/** Ends the authenticated dashboard cookie session. */
export async function logout(antiforgeryToken: string): Promise<void> {
  await createClient().request('/auth/logout', {
    method: 'POST',
    headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
  });
}
