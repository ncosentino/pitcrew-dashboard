import {
  supportIdentitySchema,
  supportSessionSchema,
  type SupportIdentity,
  type SupportSession,
} from '../../src/features/support/supportApi';

export const supportNodeIds = {
  active: '10000000-0000-4000-8000-000000000001',
  revoked: '10000000-0000-4000-8000-000000000002',
} as const;

export const supportSessionIds = {
  active: '20000000-0000-4000-8000-000000000001',
  completed: '20000000-0000-4000-8000-000000000002',
} as const;

export function buildSupportIdentity(overrides: Partial<SupportIdentity> = {}): SupportIdentity {
  return supportIdentitySchema.parse({
    nodeId: supportNodeIds.active,
    displayName: 'Primary build host',
    status: 'Active',
    createdAt: '2026-08-20T10:00:00+00:00',
    revokedAt: null,
    lastPollAt: '2026-08-27T15:00:00+00:00',
    lastResultAt: '2026-08-27T14:55:00+00:00',
    capabilityVersion: 1,
    ...overrides,
  });
}

export function buildSupportSession(overrides: Partial<SupportSession> = {}): SupportSession {
  return supportSessionSchema.parse({
    sessionId: supportSessionIds.active,
    nodeId: supportNodeIds.active,
    diagnosticMode: 'HostPressure',
    profileId: 'general-purpose',
    capability: 'pitcrew.diagnostics.snapshot.v1',
    requestDigest: 'a'.repeat(64),
    nodeSigningKeyFingerprint: 'b'.repeat(64),
    status: 'Dispatched',
    requestedAt: '2026-08-27T15:00:00+00:00',
    expiresAt: '2026-08-27T15:15:00+00:00',
    dispatchedAt: '2026-08-27T15:00:10+00:00',
    rejectionDisposition: null,
    result: null,
    ...overrides,
  });
}
