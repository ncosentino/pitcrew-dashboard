import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';
import {
  dashboardUserSchema,
  tenantRoleSchema,
  type DashboardUser,
  type TenantRole,
} from '@/core/auth/sessionApi';

const offsetDateTimeSchema = z.string().datetime({ offset: true });
const tenantMemberSchema = z.object({
  user: dashboardUserSchema,
  role: tenantRoleSchema,
  createdAt: offsetDateTimeSchema,
});
const tenantMembersSchema = z.array(tenantMemberSchema);
const dashboardUsersSchema = z.array(dashboardUserSchema);
const enrollmentCodeResponseSchema = z.object({
  enrollmentCodeId: z.string().uuid(),
  code: z.string(),
  expiresAt: offsetDateTimeSchema,
});
const diagnosticCredentialSchema = z.object({
  credentialId: z.string().uuid(),
  label: z.string(),
  createdByGitHubUserId: z.string(),
  createdAt: offsetDateTimeSchema,
  expiresAt: offsetDateTimeSchema,
  revokedAt: offsetDateTimeSchema.nullable(),
  revokedByGitHubUserId: z.string().nullable(),
  rotatedFromCredentialId: z.string().uuid().nullable(),
  lastUsedAt: offsetDateTimeSchema.nullable(),
  useCount: z.number().int().nonnegative(),
  nodeIds: z.array(z.string().uuid()),
  profileIds: z.array(z.string()),
});
const diagnosticCredentialsSchema = z.array(diagnosticCredentialSchema);
const diagnosticCredentialCreatedSchema = z.object({
  credential: diagnosticCredentialSchema,
  value: z.string().min(1),
});

/** Persisted tenant membership returned to an owner. */
export type TenantMember = z.infer<typeof tenantMemberSchema>;
/** One-time connector enrollment code returned only at creation. */
export type EnrollmentCodeResponse = z.infer<typeof enrollmentCodeResponseSchema>;
/** Non-secret metadata for one scoped diagnostic credential. */
export type DiagnosticCredential = z.infer<typeof diagnosticCredentialSchema>;
/** Raw credential returned only by creation or rotation. */
export type DiagnosticCredentialCreated = z.infer<typeof diagnosticCredentialCreatedSchema>;

function createClient(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
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

/** Lists scoped diagnostic credential metadata for one tenant. */
export async function getDiagnosticCredentials(
  tenantId: string,
  signal: AbortSignal,
): Promise<readonly DiagnosticCredential[]> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/diagnostic-credentials`,
    {
      method: 'GET',
      schema: diagnosticCredentialsSchema,
      signal,
    },
  );
}

/** Creates one expiring read-only diagnostic credential. */
export async function createDiagnosticCredential(
  tenantId: string,
  label: string,
  expiresAt: string,
  nodeIds: readonly string[],
  profileIds: readonly string[],
  antiforgeryToken: string,
): Promise<DiagnosticCredentialCreated> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/diagnostic-credentials`,
    {
      method: 'POST',
      body: { label, expiresAt, nodeIds, profileIds },
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
      schema: diagnosticCredentialCreatedSchema,
    },
  );
}

/** Revokes one diagnostic credential immediately. */
export async function revokeDiagnosticCredential(
  tenantId: string,
  credentialId: string,
  antiforgeryToken: string,
): Promise<void> {
  await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/diagnostic-credentials/${encodeURIComponent(credentialId)}/revoke`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    },
  );
}

/** Replaces one active diagnostic credential while preserving its scope and expiry. */
export async function rotateDiagnosticCredential(
  tenantId: string,
  credentialId: string,
  antiforgeryToken: string,
): Promise<DiagnosticCredentialCreated> {
  return await createClient().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/diagnostic-credentials/${encodeURIComponent(credentialId)}/rotate`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
      schema: diagnosticCredentialCreatedSchema,
    },
  );
}
