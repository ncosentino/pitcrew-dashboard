import { describe, expect, it } from 'vitest';

import { operationalIncidentSchema, type ManagerObservedState } from '@/core/fleet';

import {
  summarizeNodeWorkload,
  summarizeProfileAttention,
  summarizeProfileWorkload,
} from './profileWorkspace';

function profile(overrides: Partial<ManagerObservedState> = {}): ManagerObservedState {
  return {
    schemaVersion: 1,
    managerContractVersion: 10,
    profileId: 'build',
    managerInstanceId: 'manager-build',
    managerStatus: 'running',
    observedAt: '2026-08-27T12:00:00+00:00',
    scope: 'repository',
    generation: 1,
    desiredStateHash: 'a'.repeat(64),
    desiredStateStatus: 'accepted',
    configuredSlots: 2,
    desiredSlots: 2,
    activeSlots: 2,
    eligibleSlots: 2,
    drainingSlots: 0,
    slots: [],
    resourceTelemetry: null,
    resourcePolicy: null,
    operationJournal: null,
    subsystemHealth: null,
    capacityEvidence: null,
    update: null,
    ...overrides,
  };
}

describe('profile workspace evidence summaries', () => {
  it('does not convert unknown worker activity or missing job statistics to zero', () => {
    const summary = summarizeProfileWorkload(
      profile({
        slots: [
          {
            key: 'build-1',
            repository: null,
            desired: true,
            processRunning: true,
            state: 'online',
            failureCount: 0,
            backoffSeconds: 0,
            updatedAt: null,
            activity: null,
            imageId: null,
            lastExit: null,
            runnerNameHash: null,
          },
        ],
      }),
    );

    expect(summary.busyLabel).toBe('0 confirmed busy');
    expect(summary.busyDetail).toContain('activity is unavailable');
    expect(summary.runningJobsLabel).toBe('Unavailable');
  });

  it('reports partial node job coverage instead of a fabricated total', () => {
    const summary = summarizeNodeWorkload([
      profile({
        autoscaling: {
          mode: 'scale-set',
          status: 'running',
          minimumIdleSlots: 0,
          maximumSlots: 2,
          targetSlots: 2,
          assignedJobs: 1,
          runningJobs: 1,
          availableJobs: 0,
          idleRunners: 1,
          busyRunners: 1,
          scaleDownDelaySeconds: 30,
          scaleSetCount: 1,
          scaleDownAt: null,
          lastError: null,
          maximumActiveWorkers: null,
          targets: null,
        },
      }),
      profile({ profileId: 'fixed' }),
    ]);

    expect(summary.runningJobsLabel).toBe('1');
    expect(summary.runningJobsDetail).toBe(
      '1 of 2 profiles report aggregate running-job statistics',
    );
  });

  it('keeps an empty node workload unavailable without claiming complete coverage', () => {
    const summary = summarizeNodeWorkload([]);

    expect(summary.runningJobsLabel).toBe('Unavailable');
    expect(summary.runningJobsDetail).toBe('No profiles report aggregate running-job statistics');
  });

  it.each(['starting', 'stopping'] as const)(
    'treats the valid %s manager lifecycle as attention',
    (managerStatus) => {
      expect(summarizeProfileAttention(profile({ managerStatus }), [])).toMatchObject({
        label: `Manager ${managerStatus}`,
        tone: 'caution',
        task: 'diagnostics',
        rank: 3,
      });
    },
  );

  it('routes coordinator withholding evidence to the capacity workspace', () => {
    const summary = summarizeProfileAttention(
      profile({
        managerContractVersion: 19,
        hostAdmission: {
          status: 'available',
          namespace: 'primary',
          epoch: 3,
          decisionSequence: 42,
          capacityUnits: 12,
          safetyMarginUnits: 2,
          effectiveTotalUnits: 10,
          availableUnits: 4,
          hostPolicyFingerprint: 'host-policy',
          accounting: {
            unitCost: 2,
            reservedUnits: 4,
            borrowable: false,
            profilePolicyFingerprint: 'profile-policy',
            activeUnits: 5,
            provisionalUnits: 0,
            heldUnits: 5,
            borrowedUnits: 1,
            pendingUnits: 4,
            withheldUnits: 4,
            allocatableUnits: 0,
            allocatableWorkers: 0,
            theoreticalMaximumUnits: 10,
            theoreticalMaximumWorkers: 5,
            withholdingReason: 'protected-reservation',
          },
          lastDecision: null,
        },
      }),
      [],
    );

    expect(summary).toMatchObject({
      label: 'Profile admission withheld',
      description: expect.stringContaining('not borrowable by this profile'),
      tone: 'caution',
      task: 'capacity',
      rank: 4,
    });
  });

  it('prioritizes explicit degraded evidence and names its owning task', () => {
    const summary = summarizeProfileAttention(
      profile({
        update: {
          status: 'degraded',
          targetImage: null,
          targetImageId: null,
          targetRevision: 'c'.repeat(64),
          currentWorkers: 1,
          staleWorkers: 1,
          lastError: 'Rollout failed.',
        },
      }),
      [],
    );

    expect(summary).toMatchObject({
      label: 'Worker rollout degraded',
      tone: 'caution',
      task: 'workers',
      rank: 2,
    });
    expect(summary.category).toBe('confirmed-problem');
  });

  it('separates active changes from evidence gaps', () => {
    const activeChange = summarizeProfileAttention(
      profile({
        update: {
          status: 'rolling',
          targetImage: null,
          targetImageId: null,
          targetRevision: 'c'.repeat(64),
          currentWorkers: 1,
          staleWorkers: 1,
          lastError: null,
        },
      }),
      [],
    );
    const evidenceGap = summarizeProfileAttention(
      profile({
        resourceTelemetry: {
          sampledAt: '2026-08-27T12:00:00+00:00',
          status: 'partial',
          host: null,
          manager: null,
        },
      }),
      [],
    );

    expect(activeChange.category).toBe('active-change');
    expect(activeChange.categories).toContain('active-change');
    expect(evidenceGap.category).toBe('evidence-gap');
    expect(evidenceGap.categories).toContain('evidence-gap');
  });

  it('keeps ended monitoring in retained history instead of classifying it as an evidence gap', () => {
    const incident = operationalIncidentSchema.parse({
      incidentId: 'd6235ec4-2a15-4f91-a9e0-811152869a54',
      nodeId: 'a6235ec4-2a15-4f91-a9e0-811152869a51',
      profileId: 'build',
      kind: 'manager-health',
      severity: 'warning',
      status: 'triggered',
      title: 'Manager report ended',
      summary: 'Monitoring ended after the reporting window closed.',
      reason: 'monitoring-ended',
      evidence: null,
      link: '/tenants/local/incidents/d6235ec4-2a15-4f91-a9e0-811152869a54',
      firstObservedAt: '2026-08-27T11:00:00+00:00',
      triggeredAt: '2026-08-27T11:00:00+00:00',
      lastObservedAt: '2026-08-27T12:00:00+00:00',
      acknowledgedAt: null,
      acknowledgedByGitHubUserId: null,
      resolvedAt: null,
      conditionState: 'monitoring-ended',
      operatorState: 'unowned',
      currentSeverity: null,
    });

    const summary = summarizeProfileAttention(profile(), [incident]);

    expect(summary.categories).toContain('retained');
  });

  it('treats a confirmed manager stop as caution action rather than critical severity', () => {
    const summary = summarizeProfileAttention(profile({ managerStatus: 'stopped' }), []);

    expect(summary).toMatchObject({
      label: 'Manager stopped',
      tone: 'caution',
      category: 'confirmed-problem',
      rank: 2,
    });
  });
});
