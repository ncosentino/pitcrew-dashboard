import { describe, expect, it } from 'vitest';

import { type ManagerEvent, type ManagerOperationJournal } from './fleetApi';
import {
  describeCapacityDeficit,
  describeJournalAvailability,
  describeManagerEvent,
  describeSubsystemHealth,
  orderedManagerEvents,
} from './managerEvidence';

function event(overrides: Partial<ManagerEvent> = {}): ManagerEvent {
  return {
    sequence: 41,
    managerInstanceId: 'manager-default',
    observedAt: '2026-07-19T18:29:00+00:00',
    subsystem: 'docker',
    operation: 'docker-run',
    target: 'repo-default-000001',
    outcome: 'failed',
    durationMilliseconds: 1_200,
    attempt: 3,
    consecutiveFailures: 2,
    retryAt: null,
    reason: 'docker-failed',
    evidence: 'Docker refused to start the worker container.',
    ...overrides,
  };
}

function journal(overrides: Partial<ManagerOperationJournal> = {}): ManagerOperationJournal {
  return {
    status: 'current',
    capacity: 32,
    highestSequence: 41,
    droppedEvents: 0,
    events: [event()],
    ...overrides,
  };
}

describe('orderedManagerEvents', () => {
  it('deduplicates by durable contract identity rather than by observing manager', () => {
    const events = orderedManagerEvents(
      journal({
        events: [
          event({ sequence: 41 }),
          event({ sequence: 41, managerInstanceId: 'manager-restarted' }),
          event({ sequence: 42, managerInstanceId: 'manager-restarted' }),
        ],
        highestSequence: 42,
      }),
    );

    expect(events.map((candidate) => candidate.sequence)).toEqual([42, 41]);
    expect(events[1].managerInstanceId).toBe('manager-default');
  });

  it('returns no events when the journal is unreported', () => {
    expect(orderedManagerEvents(null)).toEqual([]);
  });
});

describe('describeJournalAvailability', () => {
  it.each([
    [null, 'unreported', 'does not publish a durable operation journal'],
    [
      journal({ status: 'unavailable', events: [], highestSequence: null }),
      'unavailable',
      'could not read or restore',
    ],
    [journal({ status: 'truncated', droppedEvents: 6 }), 'truncated', 'discarded 6 older'],
    [journal({ events: [] }), 'current', 'no notable operation'],
  ])('describes %#', (value, availability, message) => {
    const summary = describeJournalAvailability(value);

    expect(summary.availability).toBe(availability);
    expect(summary.description).toContain(message);
  });
});

describe('describeManagerEvent', () => {
  it('labels manager-supplied evidence rather than a dashboard diagnosis', () => {
    const description = describeManagerEvent(
      event({ retryAt: '2026-07-19T18:30:30+00:00', outcome: 'retry-scheduled' }),
    );

    expect(description).toContain('The manager reported docker-run');
    expect(description).toContain('Reason: docker-failed.');
    expect(description).toContain('Manager evidence:');
  });

  it('keeps an unmeasured duration distinct from a measured zero', () => {
    expect(describeManagerEvent(event({ durationMilliseconds: null }))).toContain(
      'duration unavailable',
    );
    expect(describeManagerEvent(event({ durationMilliseconds: 0 }))).toContain('0 ms');
  });
});

describe('describeSubsystemHealth', () => {
  it('never claims the subsystem itself is healthy', () => {
    const summary = describeSubsystemHealth(
      {
        state: 'healthy',
        observedAt: '2026-07-19T18:30:00+00:00',
        consecutiveFailures: 0,
        retryAt: null,
        lastSuccess: {
          operation: 'docker-ping',
          observedAt: '2026-07-19T18:29:00+00:00',
          durationMilliseconds: 4,
          reason: 'none',
          evidence: null,
        },
        lastFailure: null,
      },
      'Docker',
    );

    expect(summary.label).toBe('Healthy');
    expect(summary.description).toContain('not the health of Docker itself');
  });

  it('keeps unknown and unreported states distinct from healthy', () => {
    expect(describeSubsystemHealth(null, 'GitHub').description).toContain(
      'unavailable rather than healthy',
    );
    expect(
      describeSubsystemHealth(
        {
          state: 'unknown',
          observedAt: '2026-07-19T18:30:00+00:00',
          consecutiveFailures: 0,
          retryAt: null,
          lastSuccess: null,
          lastFailure: null,
        },
        'GitHub',
      ).description,
    ).toContain('unknown rather than healthy');
  });
});

describe('describeCapacityDeficit', () => {
  it('measures a shortfall against the activation target only', () => {
    const summary = describeCapacityDeficit({
      observedAt: '2026-07-19T18:30:00+00:00',
      freshness: 'current',
      targetSlots: 3,
      activeWorkers: 2,
      startingWorkers: 0,
      drainingWorkers: 0,
      cleanupPendingWorkers: 0,
      eligibleWorkers: 1,
      localDeficit: 1,
      eligibilityDeficit: 2,
      reason: 'docker-failed',
      evidence: 'Docker refused to start the worker container.',
    });

    expect(summary.label).toBe('1 short of target');
    expect(summary.description).toContain('activation target of 3');
    expect(summary.description).toContain('The manager-supplied blocking reason is docker-failed.');
  });

  it('keeps unavailable evidence distinct from a measured zero shortfall', () => {
    const unavailable = describeCapacityDeficit({
      observedAt: '2026-07-19T18:30:00+00:00',
      freshness: 'unavailable',
      targetSlots: 3,
      activeWorkers: 0,
      startingWorkers: 0,
      drainingWorkers: 0,
      cleanupPendingWorkers: 0,
      eligibleWorkers: null,
      localDeficit: 0,
      eligibilityDeficit: null,
      reason: 'unknown',
      evidence: null,
    });
    const measuredZero = describeCapacityDeficit({
      observedAt: '2026-07-19T18:30:00+00:00',
      freshness: 'current',
      targetSlots: 3,
      activeWorkers: 3,
      startingWorkers: 0,
      drainingWorkers: 0,
      cleanupPendingWorkers: 0,
      eligibleWorkers: 0,
      localDeficit: 0,
      eligibilityDeficit: 3,
      reason: 'none',
      evidence: null,
    });

    expect(unavailable.label).toBe('Unavailable');
    expect(unavailable.description).toContain('unavailable rather than zero');
    expect(measuredZero.label).toBe('No reported shortfall');
    expect(measuredZero.description).toContain('0 workers are eligible');
  });
});
