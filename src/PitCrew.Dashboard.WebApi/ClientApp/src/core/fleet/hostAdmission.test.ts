import { describe, expect, it } from 'vitest';

import { describeHostAdmission, summarizeNodeHostAdmission } from './hostAdmission';
import {
  type HostAdmissionAccounting,
  type HostAdmissionState,
  type ManagerObservedState,
  managerObservedStateSchema,
} from './fleetApi';

const availableAccounting: HostAdmissionAccounting = {
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
};
const availableAdmission: HostAdmissionState = {
  status: 'available',
  namespace: 'primary',
  epoch: 3,
  decisionSequence: 42,
  capacityUnits: 12,
  safetyMarginUnits: 2,
  effectiveTotalUnits: 10,
  availableUnits: 4,
  hostPolicyFingerprint: 'host-policy',
  accounting: availableAccounting,
  lastDecision: null,
};

function profile(hostAdmission: HostAdmissionState | null): ManagerObservedState {
  return {
    schemaVersion: 1,
    managerContractVersion: hostAdmission == null ? 17 : 18,
    profileId: 'control',
    managerInstanceId: 'manager-control',
    managerStatus: 'running',
    observedAt: '2026-08-09T06:00:00+00:00',
    scope: 'repo',
    generation: 1,
    desiredStateHash: null,
    desiredStateStatus: 'accepted',
    desiredSlots: 0,
    activeSlots: 0,
    eligibleSlots: 0,
    drainingSlots: 0,
    slots: [],
    resourceTelemetry: null,
    configuredSlots: 0,
    autoscaling: null,
    resourcePolicy: null,
    operationJournal: null,
    subsystemHealth: null,
    capacityEvidence: null,
    update: null,
    hostAdmission,
  };
}

function contractProfile(
  managerContractVersion: number,
  hostAdmission?: unknown,
): unknown {
  return {
    schemaVersion: 1,
    managerContractVersion,
    profileId: 'control',
    managerInstanceId: 'manager-control',
    managerStatus: 'running',
    observedAt: '2026-08-09T06:00:00+00:00',
    scope: 'repo',
    generation: 1,
    desiredStateHash: null,
    desiredStateStatus: 'accepted',
    desiredSlots: 0,
    activeSlots: 0,
    eligibleSlots: 0,
    drainingSlots: 0,
    slots: [],
    resourceTelemetry: {
      sampledAt: '2026-08-09T06:00:00+00:00',
      status: 'unavailable',
      host: null,
      manager: null,
      hostPressure: {
        status: 'unavailable',
        source: 'docker-host',
        cpuUtilizationPercent: null,
        load1: null,
        load5: null,
        load15: null,
        memoryTotalBytes: null,
        memoryAvailableBytes: null,
        swapUsedBytes: null,
        cpuPressureSomeAvg10: null,
        cpuPressureFullAvg10: null,
        memoryPressureSomeAvg10: null,
        memoryPressureFullAvg10: null,
        ioPressureSomeAvg10: null,
        ioPressureFullAvg10: null,
      },
    },
    configuredSlots: 0,
    autoscaling: null,
    resourcePolicy: null,
    operationJournal: {
      status: 'current',
      capacity: 32,
      highestSequence: null,
      droppedEvents: 0,
      events: [],
    },
    subsystemHealth: {
      docker: {
        state: 'unknown',
        observedAt: '2026-08-09T06:00:00+00:00',
        consecutiveFailures: 0,
        retryAt: null,
        lastSuccess: null,
        lastFailure: null,
      },
      github: {
        state: 'unknown',
        observedAt: '2026-08-09T06:00:00+00:00',
        consecutiveFailures: 0,
        retryAt: null,
        lastSuccess: null,
        lastFailure: null,
      },
    },
    capacityEvidence: {
      fixed: {
        observedAt: '2026-08-09T06:00:00+00:00',
        freshness: 'current',
        targetSlots: 0,
        activeWorkers: 0,
        startingWorkers: 0,
        drainingWorkers: 0,
        cleanupPendingWorkers: 0,
        eligibleWorkers: 0,
        localDeficit: 0,
        eligibilityDeficit: 0,
        reason: 'none',
        evidence: null,
      },
      targets: [],
    },
    update: null,
    ...(hostAdmission === undefined ? {} : { hostAdmission }),
  };
}

describe('describeHostAdmission', () => {
  it('keeps missing evidence unavailable rather than disabled', () => {
    expect(describeHostAdmission(null)).toEqual({
      status: 'unavailable',
      label: 'Unavailable',
      description: 'This manager does not report host-admission evidence.',
    });
  });

  it('describes a disabled profile as not configured', () => {
    expect(
      describeHostAdmission({
        status: 'disabled',
        namespace: null,
        epoch: null,
        decisionSequence: null,
        capacityUnits: null,
        safetyMarginUnits: null,
        effectiveTotalUnits: null,
        availableUnits: null,
        hostPolicyFingerprint: null,
        accounting: null,
        lastDecision: null,
      }),
    ).toMatchObject({ status: 'disabled', label: 'Not configured' });
  });
});

describe('summarizeNodeHostAdmission', () => {
  it('keeps an empty node unavailable', () => {
    expect(summarizeNodeHostAdmission([])).toEqual({
      status: 'unavailable',
      configuredProfiles: 0,
      borrowedUnits: null,
      withheldUnits: null,
    });
  });

  it('sums complete borrowed and withheld accounting', () => {
    const summary = summarizeNodeHostAdmission([
      profile(availableAdmission),
      profile({
        ...availableAdmission,
        accounting: {
          ...availableAccounting,
          activeUnits: 6,
          heldUnits: 6,
          borrowedUnits: 2,
          pendingUnits: 0,
          withheldUnits: 0,
        },
      }),
    ]);

    expect(summary).toEqual({
      status: 'available',
      configuredProfiles: 2,
      borrowedUnits: 3,
      withheldUnits: 4,
    });
  });

  describe('managerObservedStateSchema host admission contract', () => {
    it('requires complete host admission starting at manager contract 18', () => {
      expect(
        managerObservedStateSchema.safeParse(contractProfile(18, availableAdmission)).success,
      ).toBe(true);
      expect(managerObservedStateSchema.safeParse(contractProfile(18)).success).toBe(false);
    });

    it('keeps manager contract 17 readable without host admission', () => {
      expect(managerObservedStateSchema.safeParse(contractProfile(17)).success).toBe(true);
    });

    it('accepts adopt decisions and rejects unknown commands', () => {
      const adoptedDecision = {
        sequence: 43,
        command: 'adopt' as const,
        granted: true,
        failureCategory: null,
        decidedAtUnixNano: 1_754_719_500_000_000_000,
      };
      const adopted = contractProfile(18, {
        ...availableAdmission,
        lastDecision: adoptedDecision,
      });

      expect(managerObservedStateSchema.safeParse(adopted).success).toBe(true);
      const unknownCommand = contractProfile(18, {
        ...availableAdmission,
        lastDecision: {
          ...adoptedDecision,
          command: 'unknown',
        },
      });
      expect(
        managerObservedStateSchema.safeParse(unknownCommand).success,
      ).toBe(false);
    });
  });

  it('does not fabricate totals when one profile is unavailable', () => {
    const summary = summarizeNodeHostAdmission([
      profile(availableAdmission),
      profile({
        status: 'unavailable',
        namespace: 'primary',
        epoch: null,
        decisionSequence: null,
        capacityUnits: null,
        safetyMarginUnits: null,
        effectiveTotalUnits: null,
        availableUnits: null,
        hostPolicyFingerprint: null,
        accounting: null,
        lastDecision: null,
      }),
    ]);

    expect(summary).toMatchObject({
      status: 'unavailable',
      borrowedUnits: null,
      withheldUnits: null,
    });
  });

  it('does not publish partial totals when one profile is legacy', () => {
    const summary = summarizeNodeHostAdmission([profile(availableAdmission), profile(null)]);

    expect(summary).toMatchObject({
      status: 'unavailable',
      configuredProfiles: 1,
      borrowedUnits: null,
      withheldUnits: null,
    });
  });
});
