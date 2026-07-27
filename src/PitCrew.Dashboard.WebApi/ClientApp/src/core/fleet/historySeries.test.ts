import { describe, expect, it } from 'vitest';

import type { ProfileHistory, ProfileTelemetryRollup, ProfileTelemetrySample } from './historyApi';
import {
  buildDeficitReasonChanges,
  buildHistorySeries,
  describeHistoryAvailability,
  describeHistoryJournal,
} from './historySeries';

function sample(overrides: Partial<ProfileTelemetrySample> = {}): ProfileTelemetrySample {
  return {
    observedAt: '2026-07-26T12:00:00+00:00',
    sampledAt: '2026-07-26T12:00:00+00:00',
    telemetryStatus: 'available',
    managerInstanceId: 'manager-default',
    managerStatus: 'running',
    generation: 4,
    desiredSlots: 3,
    activeSlots: 2,
    drainingSlots: 0,
    configuredSlots: 6,
    eligibleSlots: 2,
    targetSlots: 3,
    maximumSlots: 6,
    assignedJobs: 1,
    runningJobs: 1,
    availableJobs: 0,
    idleRunners: 1,
    busyRunners: 1,
    localRunningWorkers: 2,
    managerCpuCores: 0.25,
    managerMemoryBytes: 104_857_600,
    managerPids: 12,
    hostLogicalProcessorCount: 8,
    hostMemoryBytes: 17_179_869_184,
    workerCpuCores: 1.5,
    workerMemoryBytes: 2_147_483_648,
    workerPids: 64,
    networkRxBytes: 1_024,
    networkTxBytes: 2_048,
    blockReadBytes: 4_096,
    blockWriteBytes: 8_192,
    exitReports: 1,
    adverseExitReports: 0,
    localCapacityDeficit: 1,
    eligibilityCapacityDeficit: 0,
    capacityDeficitReason: 'docker-failed',
    capacityDeficitFreshness: 'current',
    ...overrides,
  };
}

function rollup(overrides: Partial<ProfileTelemetryRollup> = {}): ProfileTelemetryRollup {
  return {
    bucketStart: '2026-07-26T12:00:00+00:00',
    sampleCount: 4,
    maximumDesiredSlots: 3,
    maximumActiveSlots: 2,
    maximumDrainingSlots: 0,
    maximumEligibleSlots: 2,
    maximumLocalRunningWorkers: 2,
    maximumManagerCpuCores: 0.5,
    maximumManagerMemoryBytes: 104_857_600,
    maximumManagerPids: 12,
    maximumWorkerCpuCores: 1.5,
    maximumWorkerMemoryBytes: 2_147_483_648,
    maximumWorkerPids: 64,
    maximumNetworkRxBytes: 1_024,
    maximumNetworkTxBytes: 2_048,
    maximumBlockReadBytes: 4_096,
    maximumBlockWriteBytes: 8_192,
    maximumExitReports: 1,
    maximumAdverseExitReports: 0,
    maximumLocalCapacityDeficit: 1,
    ...overrides,
  };
}

function history(overrides: Partial<ProfileHistory> = {}): ProfileHistory {
  return {
    profileId: 'default',
    samples: [sample()],
    rollups: [rollup()],
    events: [],
    pointsTruncated: false,
    eventsTruncated: false,
    journal: {
      status: 'current',
      capacity: 32,
      managerHighestSequence: 41,
      storedLowestSequence: 1,
      storedHighestSequence: 41,
      managerDroppedEvents: 0,
      missedEvents: 0,
      undeliveredEvents: 0,
      updatedAt: '2026-07-26T12:00:00+00:00',
    },
    ...overrides,
  };
}

describe('buildHistorySeries', () => {
  it('keeps local worker counts separate from control-plane runner counts', () => {
    const groups = buildHistorySeries(history(), 'raw');
    const counts = groups.find((group) => group.key === 'counts');

    expect(counts?.series.map((series) => series.key)).toContain('local-running-workers');
    expect(counts?.series.map((series) => series.key)).toContain('eligible-slots');
    expect(
      counts?.series.find((series) => series.key === 'local-running-workers')?.points[0]?.value,
    ).toBe(2);
  });

  it('preserves an unavailable measurement instead of plotting a measured zero', () => {
    const groups = buildHistorySeries(
      history({ samples: [sample({ eligibleSlots: null, workerCpuCores: 0 })] }),
      'raw',
    );
    const counts = groups.find((group) => group.key === 'counts');
    const cpu = groups.find((group) => group.key === 'cpu');

    expect(counts?.series.find((series) => series.key === 'eligible-slots')?.points[0]?.value).toBe(
      null,
    );
    expect(cpu?.series.find((series) => series.key === 'worker-cpu')?.points[0]?.value).toBe(0);
  });

  it('projects hourly rollups by bucket start when the hourly resolution is served', () => {
    const groups = buildHistorySeries(history(), 'hourly');
    const cpu = groups.find((group) => group.key === 'cpu');
    const managerCpu = cpu?.series.find((series) => series.key === 'manager-cpu');

    expect(managerCpu?.points).toEqual([{ at: '2026-07-26T12:00:00+00:00', value: 0.5 }]);
  });

  it('covers every series required by the historical telemetry views', () => {
    const groups = buildHistorySeries(history(), 'raw');

    expect(groups.map((group) => group.key)).toEqual([
      'capacity',
      'counts',
      'cpu',
      'memory',
      'pids',
      'network',
      'block-io',
      'exits',
      'deficits',
    ]);
  });
});

describe('buildDeficitReasonChanges', () => {
  it('records only the observations where manager deficit evidence changed', () => {
    const changes = buildDeficitReasonChanges(
      history({
        samples: [
          sample({ observedAt: '2026-07-26T12:00:00+00:00' }),
          sample({ observedAt: '2026-07-26T12:00:15+00:00' }),
          sample({
            observedAt: '2026-07-26T12:00:30+00:00',
            capacityDeficitReason: 'registration-missing',
          }),
        ],
      }),
    );

    expect(changes.map((change) => change.at)).toEqual([
      '2026-07-26T12:00:30+00:00',
      '2026-07-26T12:00:00+00:00',
    ]);
  });

  it('keeps unreported deficit evidence distinct from a measured zero shortfall', () => {
    const changes = buildDeficitReasonChanges(
      history({
        samples: [
          sample({ capacityDeficitReason: null, localCapacityDeficit: null }),
          sample({
            observedAt: '2026-07-26T12:00:15+00:00',
            capacityDeficitReason: null,
            localCapacityDeficit: 0,
          }),
        ],
      }),
    );

    expect(changes).toHaveLength(2);
    expect(changes[1]?.localDeficit).toBe(null);
    expect(changes[0]?.localDeficit).toBe(0);
  });
});

describe('describeHistoryAvailability', () => {
  it('reports an empty range as unavailable rather than as zero activity', () => {
    expect(describeHistoryAvailability(history({ samples: [] }), 'raw').status).toBe('unavailable');
  });

  it('reports truncation explicitly', () => {
    const availability = describeHistoryAvailability(history({ pointsTruncated: true }), 'raw');

    expect(availability.status).toBe('partial');
    expect(availability.description).toContain('most recent');
  });
});

describe('describeHistoryJournal', () => {
  it('reports missed durable sequences as an explicit gap', () => {
    const journal = describeHistoryJournal(
      history({
        journal: {
          ...history().journal,
          missedEvents: 4,
        },
      }),
    );

    expect(journal.status).toBe('partial');
    expect(journal.description).toContain('4 durable sequences');
  });

  it('reports undelivered manager sequences as an explicit gap', () => {
    const journal = describeHistoryJournal(
      history({ journal: { ...history().journal, undeliveredEvents: 2 } }),
    );

    expect(journal.description).toContain('2 sequences');
  });

  it('reports an unreported journal as unavailable rather than empty', () => {
    expect(
      describeHistoryJournal(history({ journal: { ...history().journal, status: 'unreported' } }))
        .status,
    ).toBe('unavailable');
  });
});
