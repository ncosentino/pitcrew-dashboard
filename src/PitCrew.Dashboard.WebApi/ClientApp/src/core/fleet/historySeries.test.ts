import { describe, expect, it } from 'vitest';

import type {
  ProfileCapacityDeficitObservation,
  ProfileHistory,
  ProfileTelemetryRollup,
  ProfileTelemetrySample,
} from './historyApi';
import {
  buildDeficitReasonChanges,
  buildHistorySeries,
  describeDeficitEvidence,
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
    maximumEligibilityCapacityDeficit: 0,
    maximumTargetSlots: 3,
    maximumAssignedJobs: 1,
    maximumIdleRunners: 1,
    maximumBusyRunners: 1,
    ...overrides,
  };
}

function deficit(
  overrides: Partial<ProfileCapacityDeficitObservation> = {},
): ProfileCapacityDeficitObservation {
  return {
    targetKey: 'repo:contoso/pitcrew',
    observedAt: '2026-07-26T12:00:00+00:00',
    repository: 'contoso/pitcrew',
    freshness: 'current',
    targetSlots: 3,
    activeWorkers: 2,
    startingWorkers: 0,
    drainingWorkers: 0,
    cleanupPendingWorkers: 0,
    eligibleWorkers: 2,
    localDeficit: 1,
    eligibilityDeficit: 0,
    reason: 'docker-failed',
    evidence: null,
    ...overrides,
  };
}

function history(overrides: Partial<ProfileHistory> = {}): ProfileHistory {
  return {
    profileId: 'default',
    samples: [sample()],
    rollups: [rollup()],
    events: [],
    subsystemHealthChanges: [],
    capacityDeficits: [deficit()],
    pointsTruncated: false,
    eventsTruncated: false,
    retention: {
      earliestRetainedSample: '2026-07-26T11:00:00+00:00',
      droppedSamples: 0,
      earliestRetainedRollup: '2026-07-26T11:00:00+00:00',
      droppedRollups: 0,
      earliestRetainedEvent: '2026-07-26T11:00:00+00:00',
      droppedEvents: 0,
      rejectedFutureSamples: 0,
    },
    journal: {
      status: 'current',
      capacity: 32,
      managerHighestSequence: 41,
      storedLowestSequence: 1,
      storedHighestSequence: 41,
      managerDroppedEvents: 0,
      missedEvents: 0,
      undeliveredEvents: 0,
      epoch: 0,
      epochResets: 0,
      rejectedFutureEvents: 0,
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

  it('labels hourly aggregates as peaks and never as hourly usage', () => {
    const groups = buildHistorySeries(history(), 'hourly');
    const network = groups.find((group) => group.key === 'network');
    const capacity = groups.find((group) => group.key === 'capacity');

    expect(capacity?.series.find((series) => series.key === 'desired-slots')?.label).toBe(
      'Peak desired slots',
    );
    expect(network?.description).toContain('not per-hour usage');
  });

  it('projects every hourly series the server persists rather than dropping them to unavailable', () => {
    const groups = buildHistorySeries(history(), 'hourly');
    const counts = groups.find((group) => group.key === 'counts');
    const deficits = groups.find((group) => group.key === 'deficits');

    expect(counts?.series.find((series) => series.key === 'busy-runners')?.points[0]?.value).toBe(
      1,
    );
    expect(
      deficits?.series.find((series) => series.key === 'eligibility-deficit')?.points[0]?.value,
    ).toBe(0);
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
  it('lists every retained target-keyed deficit change newest first', () => {
    const changes = buildDeficitReasonChanges(
      history({
        capacityDeficits: [
          deficit({ observedAt: '2026-07-26T12:00:00+00:00' }),
          deficit({
            targetKey: 'repo:contoso/other',
            repository: 'contoso/other',
            observedAt: '2026-07-26T12:00:30+00:00',
            reason: 'registration-missing',
          }),
        ],
      }),
    );

    expect(changes.map((change) => change.at)).toEqual([
      '2026-07-26T12:00:30+00:00',
      '2026-07-26T12:00:00+00:00',
    ]);
    expect(changes.map((change) => change.targetKey)).toEqual([
      'repo:contoso/other',
      'repo:contoso/pitcrew',
    ]);
  });

  it('keeps an unavailable eligibility shortfall distinct from a measured zero', () => {
    const changes = buildDeficitReasonChanges(
      history({ capacityDeficits: [deficit({ eligibilityDeficit: null })] }),
    );

    expect(changes[0]?.eligibilityDeficit).toBe(null);
  });

  it('serves the same target-keyed evidence at the hourly resolution', () => {
    const hourly = history({ samples: [] });

    expect(buildDeficitReasonChanges(hourly)).toHaveLength(1);
    expect(describeDeficitEvidence(hourly).status).toBe('available');
  });
});

describe('describeDeficitEvidence', () => {
  it('does not claim an absence of deficits when retention already deleted evidence', () => {
    const evidence = describeDeficitEvidence(
      history({
        capacityDeficits: [],
        retention: { ...history().retention, droppedSamples: 12 },
      }),
    );

    expect(evidence.status).toBe('partial');
    expect(evidence.description).toContain('retention');
  });

  it('reports an empty retained range as none retained', () => {
    expect(describeDeficitEvidence(history({ capacityDeficits: [] })).status).toBe('unavailable');
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
    expect(availability.description).toContain('older points inside the same range are hidden');
  });

  it('discloses dashboard retention deletions instead of calling an old range complete', () => {
    const availability = describeHistoryAvailability(
      history({ retention: { ...history().retention, droppedSamples: 40 } }),
      'raw',
    );

    expect(availability.description).toContain('40 older samples');
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

  it('reports a manager journal sequence reset as an explicit gap', () => {
    const journal = describeHistoryJournal(
      history({ journal: { ...history().journal, epoch: 1, epochResets: 1 } }),
    );

    expect(journal.status).toBe('partial');
    expect(journal.description).toContain('restarted its sequence');
  });

  it('reports dashboard-retention event deletions as an explicit gap', () => {
    const journal = describeHistoryJournal(
      history({ retention: { ...history().retention, droppedEvents: 7 } }),
    );

    expect(journal.status).toBe('partial');
    expect(journal.description).toContain('deleted 7 older retained events');
  });

  it('reports an unreported journal as unavailable rather than empty', () => {
    expect(
      describeHistoryJournal(history({ journal: { ...history().journal, status: 'unreported' } }))
        .status,
    ).toBe('unavailable');
  });
});
