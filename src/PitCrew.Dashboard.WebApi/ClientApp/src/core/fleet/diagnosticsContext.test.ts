import { describe, expect, it } from 'vitest';

import type { FleetNode, OperationalIncident } from './fleetApi';
import { buildDiagnosticsContext, serializeDiagnosticsContext } from './diagnosticsContext';

const nodeId = '11111111-1111-4111-8111-111111111111';
const generatedAt = '2026-08-07T13:00:00+00:00';

function createNode(overrides: Partial<FleetNode> = {}): FleetNode {
  return {
    nodeId,
    displayName: 'Zephyr',
    connectorVersion: '10.0.0',
    enrolledAt: '2026-08-01T12:00:00+00:00',
    lastSeenAt: '2026-08-07T12:55:00+00:00',
    isOnline: false,
    isRevoked: false,
    credentialRotationRequested: false,
    profiles: [],
    capacityControls: [],
    recoveryControls: [],
    hardware: null,
    connectorHealth: {
      nodeId,
      receivedAt: '2026-08-07T12:55:00+00:00',
      snapshot: {
        state: 'degraded',
        processStartedAt: '2026-08-07T11:00:00+00:00',
        updatedAt: '2026-08-07T12:55:00+00:00',
        lastAttemptAt: '2026-08-07T12:55:00+00:00',
        lastSuccessAt: '2026-08-07T12:50:00+00:00',
        activeOutageId: '22222222-2222-2222-2222-222222222222',
        activeOutageStartedAt: '2026-08-07T12:51:00+00:00',
        lastFailureAt: '2026-08-07T12:55:00+00:00',
        lastFailureCategory: 'synchronization-network',
        lastFailureProfileId: null,
        lastFailureDetail: 'Connector synchronization could not reach Dashboard.',
        consecutiveFailures: 3,
        nextRetryAt: '2026-08-07T13:00:00+00:00',
        lastRecoveredOutageId: null,
        lastRecoveredOutageStartedAt: null,
        lastRecoveredAt: null,
        lastRecoveredFailureCategory: null,
      },
    },
    ...overrides,
  };
}

function createIncident(kind: string, reason: string): OperationalIncident {
  return {
    incidentId: '33333333-3333-3333-3333-333333333333',
    nodeId,
    profileId: null,
    kind,
    severity: 'critical',
    status: 'triggered',
    title: 'Incident',
    summary: 'Summary',
    reason,
    evidence: null,
    link: `/tenants/local/nodes/${nodeId}`,
    firstObservedAt: generatedAt,
    triggeredAt: generatedAt,
    lastObservedAt: generatedAt,
    acknowledgedAt: null,
    acknowledgedByGitHubUserId: null,
    resolvedAt: null,
  };
}

describe('diagnostics context', () => {
  it('uses retained connector evidence for an offline node', () => {
    const context = buildDiagnosticsContext(createNode(), generatedAt, [
      createIncident('connector-offline', 'connector-offline'),
    ]);

    expect(context.diagnosticMode).toBe('ConnectorOffline');
    expect(context.dashboard.incident).toBe('synchronization-network');
    expect(context.dashboard.status).toBe('offline');
    expect(context.unavailableEvidence.map((item) => item.category)).not.toContain(
      'connector-health-replay',
    );
    expect(serializeDiagnosticsContext(context)).not.toMatch(
      /credential|token|command|C:\\|\/home\//iu,
    );
  });

  it('marks missing replay evidence unavailable and maps resource incidents', () => {
    const context = buildDiagnosticsContext(
      createNode({
        isOnline: true,
        connectorHealth: null,
      }),
      generatedAt,
      [createIncident('resource-memory-pressure', 'sustained-memory-pressure')],
    );

    expect(context.diagnosticMode).toBe('HostPressure');
    expect(context.dashboard.incident).toBe('sustained-memory-pressure');
    expect(context.unavailableEvidence.map((item) => item.category)).toContain(
      'connector-health-replay',
    );
  });
});
