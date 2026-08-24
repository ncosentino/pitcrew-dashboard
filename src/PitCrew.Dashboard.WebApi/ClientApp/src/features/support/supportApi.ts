import { z } from 'zod';

import { HttpClient } from '@/core/api/httpClient';

const offsetDateTimeSchema = z.string().datetime({ offset: true });

export const supportIdentitySchema = z.object({
  nodeId: z.string().uuid(),
  displayName: z.string(),
  status: z.enum(['Active', 'Revoked']),
  createdAt: offsetDateTimeSchema,
  revokedAt: offsetDateTimeSchema.nullable(),
  lastPollAt: offsetDateTimeSchema.nullable(),
  lastResultAt: offsetDateTimeSchema.nullable(),
  capabilityVersion: z.number().int().nonnegative(),
});
export const supportIdentitiesSchema = z.array(supportIdentitySchema);

export const supportAttestationSchema = z.object({
  nodeSigningPublicKeySpki: z.string(),
  payloadBase64Url: z.string(),
  signatureBase64Url: z.string(),
  signatureAlgorithm: z.literal('ES256-P1363'),
});
export const supportResultSchema = z.object({
  report: z.unknown(),
  markdown: z.string(),
  attestation: supportAttestationSchema,
});
export const supportRejectionDispositionSchema = z.enum([
  'envelope-unsupported',
  'envelope-signature-rejected',
  'envelope-payload-rejected',
  'request-malformed',
  'session-mismatch',
  'wrong-tenant-or-node',
  'unsupported-capability',
  'unsupported-diagnostic-mode',
  'request-expired',
  'invalid-nonce',
  'request-replay',
  'replay-pending',
  'broker-markdown-rejected',
  'broker-report-rejected',
  'broker-invalid-mode',
  'broker-invalid-profile',
  'broker-script-missing',
  'broker-evidence-access-denied',
  'broker-execution-failed',
  'broker-response-invalid',
  'broker-io-unavailable',
  'broker-timeout',
  'validation-rejected',
  'result-unavailable',
]);
export const supportSessionSchema = z.object({
  sessionId: z.string().uuid(),
  nodeId: z.string().uuid(),
  diagnosticMode: z.string(),
  profileId: z.string().nullable(),
  capability: z.literal('pitcrew.diagnostics.snapshot.v1'),
  requestDigest: z.string().regex(/^[a-f0-9]{64}$/),
  nodeSigningKeyFingerprint: z.string().regex(/^[a-f0-9]{64}$/),
  status: z.enum(['Queued', 'Dispatched', 'Completed', 'Rejected', 'Cancelled', 'Expired']),
  requestedAt: offsetDateTimeSchema,
  expiresAt: offsetDateTimeSchema,
  dispatchedAt: offsetDateTimeSchema.nullable(),
  rejectionDisposition: supportRejectionDispositionSchema.nullable(),
  result: supportResultSchema.nullable(),
});
export const supportSessionsSchema = z.array(supportSessionSchema);
export const createdSupportEnrollmentSchema = z.object({
  displayName: z.string(),
  enrollmentCode: z.string(),
  enrollmentExpiresAt: offsetDateTimeSchema,
});

export type SupportIdentity = z.infer<typeof supportIdentitySchema>;
export type SupportSession = z.infer<typeof supportSessionSchema>;
export type CreatedSupportEnrollment = z.infer<typeof createdSupportEnrollmentSchema>;

function client(): HttpClient {
  return new HttpClient({ baseUrl: globalThis.location.origin });
}

export async function getSupportIdentities(
  tenantId: string,
  signal?: AbortSignal,
): Promise<readonly SupportIdentity[]> {
  return await client().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/support/v1/identities`,
    {
      schema: supportIdentitiesSchema,
      signal,
    },
  );
}

export async function getSupportSessions(
  tenantId: string,
  signal?: AbortSignal,
): Promise<readonly SupportSession[]> {
  return await client().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/support/v1/sessions`,
    {
      schema: supportSessionsSchema,
      signal,
    },
  );
}

export async function getSupportSession(
  tenantId: string,
  sessionId: string,
  signal?: AbortSignal,
): Promise<SupportSession> {
  return await client().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/support/v1/sessions/${encodeURIComponent(sessionId)}`,
    {
      schema: supportSessionSchema,
      signal,
    },
  );
}

export async function createSupportSession(
  tenantId: string,
  nodeId: string,
  diagnosticMode: string,
  profileId: string | null,
  antiforgeryToken: string,
): Promise<SupportSession> {
  return await client().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/support/v1/sessions`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
      body: { nodeId, diagnosticMode, profileId, expiresInSeconds: 300 },
      schema: supportSessionSchema,
    },
  );
}

export async function createSupportEnrollment(
  tenantId: string,
  displayName: string,
  antiforgeryToken: string,
): Promise<CreatedSupportEnrollment> {
  return await client().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/support/v1/enrollment-authorizations`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
      body: { displayName },
      schema: createdSupportEnrollmentSchema,
    },
  );
}

/** Revokes one support identity without changing connector identity. */
export async function revokeSupportIdentity(
  tenantId: string,
  nodeId: string,
  antiforgeryToken: string,
): Promise<void> {
  await client().request(
    `/api/tenants/${encodeURIComponent(tenantId)}/support/v1/identities/${encodeURIComponent(nodeId)}/revoke`,
    {
      method: 'POST',
      headers: { 'X-PitCrew-Antiforgery': antiforgeryToken },
    },
  );
}
