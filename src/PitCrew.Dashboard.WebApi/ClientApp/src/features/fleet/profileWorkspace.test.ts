import { describe, expect, it } from 'vitest';

import type { ManagerObservedState } from '@/core/fleet';

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
        rank: 2,
      });
    },
  );

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
      tone: 'critical',
      task: 'workers',
      rank: 0,
    });
  });
});
