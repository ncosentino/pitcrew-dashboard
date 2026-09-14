import { describe, expect, it } from 'vitest';

import { operationalIncidentSchema } from './fleetApi';

const legacyIncident = {
  incidentId: '22222222-2222-4222-8222-222222222222',
  nodeId: '11111111-1111-4111-8111-111111111111',
  profileId: 'default',
  kind: 'capacity-deficit',
  severity: 'critical' as const,
  title: 'Capacity is below target',
  summary: 'The accepted capacity evidence reports a deficit.',
  reason: 'capacity-deficit',
  evidence: null,
  link: '/tenants/local/nodes/11111111-1111-4111-8111-111111111111/profiles/default',
  firstObservedAt: '2026-07-28T01:00:00+00:00',
  triggeredAt: '2026-07-28T01:02:00+00:00',
  lastObservedAt: '2026-07-28T01:03:00+00:00',
  acknowledgedAt: null,
  acknowledgedByGitHubUserId: null,
  resolvedAt: null,
  sourceObservedAt: null,
  dashboardReceivedAt: null,
  evaluatedAt: null,
  resolutionEvidence: null,
};

describe('operationalIncidentSchema legacy compatibility', () => {
  it('maps a legacy triggered incident to confirmed unowned truth', () => {
    const parsed = operationalIncidentSchema.parse({
      ...legacyIncident,
      status: 'triggered',
    });

    expect(parsed.conditionState).toBe('confirmed');
    expect(parsed.operatorState).toBe('unowned');
    expect(parsed.currentSeverity).toBe('critical');
  });

  it('maps a legacy acknowledged incident to confirmed acknowledged truth', () => {
    const parsed = operationalIncidentSchema.parse({
      ...legacyIncident,
      status: 'acknowledged',
      acknowledgedAt: '2026-07-28T01:04:00+00:00',
      acknowledgedByGitHubUserId: '123',
    });

    expect(parsed.conditionState).toBe('confirmed');
    expect(parsed.operatorState).toBe('acknowledged');
    expect(parsed.currentSeverity).toBe('critical');
  });

  it('maps a legacy resolved incident to unverified truth without current severity', () => {
    const parsed = operationalIncidentSchema.parse({
      ...legacyIncident,
      status: 'resolved',
      resolvedAt: '2026-07-28T01:05:00+00:00',
    });

    expect(parsed.conditionState).toBe('legacy-unverified');
    expect(parsed.operatorState).toBe('unowned');
    expect(parsed.currentSeverity).toBeNull();
    expect(parsed.lastConfirmedSeverity).toBe('critical');
  });
});
