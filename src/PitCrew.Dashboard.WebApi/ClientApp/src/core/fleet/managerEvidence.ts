import { formatSeconds, formatTime } from '@/core/formatting/formatters';

import {
  type CapacityDeficitEvidence,
  type ManagerEvent,
  type ManagerObservedState,
  type ManagerOperationJournal,
  type SubsystemHealthSummary,
  type SubsystemOperationEvidence,
  type TargetCapacityDeficitEvidence,
} from './fleetApi';

/** Availability of the bounded manager operation journal, including the unreported case. */
export type JournalAvailability = 'current' | 'truncated' | 'unavailable' | 'unreported';

/** Describes one manager-reported evidence surface without diagnosing a cause. */
export interface ManagerEvidenceSummary {
  /** Semantic status used for badge styling. */
  readonly status: string;
  /** Short operator-facing label. */
  readonly label: string;
  /** Accessible sentence describing what the manager reported. */
  readonly description: string;
}

/** Collapsed summary of the bounded manager operation journal. */
export interface ManagerOperationSummary extends ManagerEvidenceSummary {
  /** Retained events after deduplication by durable sequence. */
  readonly eventCount: number;
  /** Operation scopes whose latest retained outcome remains adverse. */
  readonly unresolvedCount: number;
}

/** One capacity-deficit projection with the scope the manager measured it against. */
export interface CapacityDeficitScope {
  /** Stable key of the fixed profile or scale-set target. */
  readonly key: string;
  /** Operator-facing scope label. */
  readonly label: string;
  /** Sanitized repository identity, or <c>null</c> for shared and fixed scopes. */
  readonly repository: string | null;
  /** Manager-reported deficit evidence for the scope. */
  readonly deficit: CapacityDeficitEvidence;
}

/**
 * Manager-reported outcomes that describe an operation the manager did not complete. A retry that
 * is still scheduled is adverse because the operation has not succeeded yet, while a recovered
 * operation is not.
 */
const adverseOutcomes: ReadonlySet<string> = new Set([
  'failed',
  'timed-out',
  'blocked',
  'retry-scheduled',
]);

function formatDuration(milliseconds: number | null): string {
  if (milliseconds == null) return 'duration unavailable';
  return milliseconds < 1_000
    ? `${milliseconds} ms`
    : formatSeconds(Math.round(milliseconds / 1_000));
}

function formatEntries(count: number): string {
  return `${count} ${count === 1 ? 'entry' : 'entries'}`;
}

/**
 * Returns the retained manager events newest first, deduplicated by the durable contract identity
 * of one profile sequence rather than by the connector heartbeat that carried them.
 */
export function orderedManagerEvents(
  journal: ManagerOperationJournal | null | undefined,
): ReadonlyArray<ManagerEvent> {
  if (journal == null) return [];
  const bySequence = new Map<number, ManagerEvent>();
  journal.events.forEach((event) => {
    if (!bySequence.has(event.sequence)) bySequence.set(event.sequence, event);
  });
  return [...bySequence.values()].sort((left, right) => right.sequence - left.sequence);
}

/** Describes whether the bounded manager journal is current, truncated, or unavailable. */
export function describeJournalAvailability(
  journal: ManagerOperationJournal | null | undefined,
): ManagerEvidenceSummary & { readonly availability: JournalAvailability } {
  if (journal == null) {
    return {
      availability: 'unreported',
      status: 'unavailable',
      label: 'Unavailable',
      description:
        'This manager does not publish a durable operation journal, so manager operations are unavailable rather than absent.',
    };
  }
  if (journal.status === 'unavailable') {
    return {
      availability: 'unavailable',
      status: 'unavailable',
      label: 'Unavailable',
      description:
        'The manager could not read or restore its durable journal, so retained operations are unavailable rather than absent.',
    };
  }
  const evictedEvents = journal.evictedEvents;
  const rejectedEvents = journal.rejectedEvents;
  const unclassifiedEvents = journal.unclassifiedEvents;
  const classified =
    evictedEvents != null && rejectedEvents != null && unclassifiedEvents != null;
  if (journal.status === 'truncated') {
    const description = classified
      ? `The manager rejected ${formatEntries(rejectedEvents)} it could not validate${
          unclassifiedEvents === 0
            ? ''
            : ` and retains ${formatEntries(unclassifiedEvents)} from legacy discarded evidence whose cause is unclassified`
        }. It also evicted ${formatEntries(evictedEvents)} through bounded retention and retains at most ${journal.capacity} events.`
      : `The manager discarded ${journal.droppedEvents} entries but this older contract does not distinguish expected eviction from rejected evidence. The journal retains at most ${journal.capacity} events.`;
    return {
      availability: 'truncated',
      status: 'partial',
      label: classified ? 'Rejected evidence' : 'Unclassified gaps',
      description,
    };
  }
  if (classified && (evictedEvents > 0 || unclassifiedEvents > 0)) {
    const legacyVerb = unclassifiedEvents === 1 ? 'predates' : 'predate';
    const legacy =
      unclassifiedEvents === 0
        ? ''
        : ` ${formatEntries(unclassifiedEvents)} from legacy discarded evidence ${legacyVerb} classified retention accounting.`;
    return {
      availability: 'current',
      status: 'available',
      label: 'Current',
      description: `The retained window is current and holds at most ${journal.capacity} events. The manager evicted ${formatEntries(evictedEvents)} through expected bounded retention.${legacy}`,
    };
  }
  return {
    availability: 'current',
    status: 'available',
    label: 'Current',
    description:
      journal.events.length === 0
        ? `The manager retains an intact window of up to ${journal.capacity} events and has recorded no notable operation.`
        : `The manager retains an intact window of up to ${journal.capacity} events.`,
  };
}

/**
 * Reports whether one manager-reported outcome describes an operation that did not complete, so a
 * collapsed summary can surface it rather than reporting a readable journal as healthy.
 */
export function isAdverseManagerOutcome(outcome: string): boolean {
  return adverseOutcomes.has(outcome);
}

function managerEventScope(event: ManagerEvent): string {
  return `${event.subsystem}\u0000${event.operation}\u0000${event.target ?? ''}`;
}

function unresolvedManagerEvents(events: ReadonlyArray<ManagerEvent>): ReadonlyArray<ManagerEvent> {
  const unresolved = new Map<string, ManagerEvent>();
  [...events]
    .sort((left, right) => left.sequence - right.sequence)
    .forEach((event) => {
      const scope = managerEventScope(event);
      if (isAdverseManagerOutcome(event.outcome)) {
        unresolved.set(scope, event);
      } else if (event.outcome === 'succeeded' || event.outcome === 'recovered') {
        unresolved.delete(scope);
      }
    });
  return [...unresolved.values()];
}

/**
 * Summarizes the bounded journal for a collapsed disclosure. The summary reports adverse manager
 * operation scopes that have no later retained success or recovery.
 */
export function summarizeManagerOperations(
  journal: ManagerOperationJournal | null | undefined,
): ManagerOperationSummary {
  const availability = describeJournalAvailability(journal);
  const events = orderedManagerEvents(journal);
  const unresolved = unresolvedManagerEvents(events);
  if (unresolved.length === 0) {
    return {
      status: availability.status,
      label: availability.label,
      description: availability.description,
      eventCount: events.length,
      unresolvedCount: 0,
    };
  }

  const unresolvedLabel = `${unresolved.length} unresolved ${
    unresolved.length === 1 ? 'operation' : 'operations'
  }`;
  const unresolvedVerb = unresolved.length === 1 ? 'has' : 'have';
  return {
    status: 'degraded',
    label: unresolvedLabel,
    description: `${availability.description} ${unresolvedLabel} ${unresolvedVerb} an adverse latest outcome with no later success or recovery in the retained window.`,
    eventCount: events.length,
    unresolvedCount: unresolved.length,
  };
}

/** Describes one manager event as manager-supplied evidence rather than a dashboard diagnosis. */
export function describeManagerEvent(event: ManagerEvent): string {
  const scope = event.target == null ? 'the profile' : event.target;
  const parts = [
    `The manager reported ${event.operation} on ${scope} as ${event.outcome} after ${formatDuration(event.durationMilliseconds)}.`,
    `Reason: ${event.reason}.`,
  ];
  if (event.attempt != null) parts.push(`Attempt ${event.attempt}.`);
  if (event.consecutiveFailures != null) {
    parts.push(`${event.consecutiveFailures} consecutive failures.`);
  }
  if (event.retryAt != null) parts.push(`Retry scheduled for ${formatTime(event.retryAt)}.`);
  parts.push(
    event.evidence == null
      ? 'The manager supplied no further evidence.'
      : `Manager evidence: ${event.evidence}`,
  );
  return parts.join(' ');
}

/**
 * Describes the health of the operations one manager performed against a subsystem without
 * claiming that the subsystem itself is healthy or unhealthy.
 */
export function describeSubsystemHealth(
  summary: SubsystemHealthSummary | null | undefined,
  subsystem: string,
): ManagerEvidenceSummary {
  if (summary == null) {
    return {
      status: 'unavailable',
      label: 'Unavailable',
      description: `This manager does not report ${subsystem} operation health, so it is unavailable rather than healthy.`,
    };
  }
  if (summary.state === 'unknown') {
    return {
      status: 'unknown',
      label: 'Unknown',
      description: `The manager has not completed a ${subsystem} operation, so its state is unknown rather than healthy.`,
    };
  }

  const backoff =
    summary.retryAt == null ? '' : ` The manager retries at ${formatTime(summary.retryAt)}.`;
  const failures =
    summary.consecutiveFailures === 0
      ? 'no consecutive failures'
      : `${summary.consecutiveFailures} consecutive failures`;
  return {
    status: summary.state,
    label:
      summary.state === 'healthy'
        ? 'Healthy'
        : summary.state === 'degraded'
          ? 'Degraded'
          : 'Unavailable',
    description: `The manager reports its own ${subsystem} operations as ${summary.state} with ${failures}. This describes operations this manager performed, not the health of ${subsystem} itself.${backoff}`,
  };
}

/** Describes one manager operation retained as subsystem evidence. */
export function describeSubsystemOperation(
  evidence: SubsystemOperationEvidence | null,
  fallback: string,
): string {
  if (evidence == null) return fallback;
  const detail =
    evidence.evidence == null
      ? 'The manager supplied no further evidence.'
      : `Manager evidence: ${evidence.evidence}`;
  return `${evidence.operation} at ${formatTime(evidence.observedAt)} after ${formatDuration(evidence.durationMilliseconds)}. Reason: ${evidence.reason}. ${detail}`;
}

/**
 * Describes a manager-reported capacity shortfall against the accepted activation target. A
 * configured autoscaling maximum is never treated as a missing-capacity threshold.
 */
export function describeCapacityDeficit(deficit: CapacityDeficitEvidence): ManagerEvidenceSummary {
  if (deficit.freshness === 'unavailable') {
    return {
      status: 'unavailable',
      label: 'Unavailable',
      description:
        'The manager could not measure capacity for this scope, so the shortfall is unavailable rather than zero.',
    };
  }

  const staleness =
    deficit.freshness === 'stale'
      ? ` The manager labelled this measurement stale as of ${formatTime(deficit.observedAt)}.`
      : '';
  const eligibilityDeficit = deficit.eligibilityDeficit ?? 0;
  const eligibility =
    deficit.eligibleWorkers == null
      ? 'Control-plane eligibility is unavailable rather than zero.'
      : `${deficit.eligibleWorkers} workers are eligible, a shortfall of ${eligibilityDeficit} against the activation target.`;
  const detail =
    deficit.evidence == null
      ? 'The manager supplied no further evidence.'
      : `Manager evidence: ${deficit.evidence}`;
  const blocking =
    deficit.reason === 'none' ? '' : ` The manager-supplied blocking reason is ${deficit.reason}.`;

  if (deficit.localDeficit === 0 && eligibilityDeficit === 0) {
    return {
      status: deficit.freshness === 'stale' ? 'stale' : 'available',
      label: 'No reported shortfall',
      description: `The manager reports ${deficit.activeWorkers} active workers against an activation target of ${deficit.targetSlots}. ${eligibility}${staleness}`,
    };
  }

  if (deficit.localDeficit === 0) {
    return {
      status: deficit.freshness === 'stale' ? 'stale' : 'degraded',
      label: `${eligibilityDeficit} short of eligibility`,
      description: `The manager reports ${deficit.activeWorkers} active workers against an activation target of ${deficit.targetSlots}, so local capacity meets the target. ${eligibility}${blocking} ${detail}${staleness}`,
    };
  }

  return {
    status: deficit.freshness === 'stale' ? 'stale' : 'degraded',
    label: `${deficit.localDeficit} short of target`,
    description: `The manager reports ${deficit.activeWorkers} active, ${deficit.startingWorkers} starting, ${deficit.drainingWorkers} draining, and ${deficit.cleanupPendingWorkers} cleanup-pending workers against an activation target of ${deficit.targetSlots}, a shortfall of ${deficit.localDeficit}. The manager-supplied blocking reason is ${deficit.reason}. ${eligibility} ${detail}${staleness}`,
  };
}

/** Lists the fixed or per-target capacity-deficit scopes reported for one profile. */
export function capacityDeficitScopes(
  profile: ManagerObservedState,
): ReadonlyArray<CapacityDeficitScope> {
  const capacityEvidence = profile.capacityEvidence;
  if (capacityEvidence == null) return [];
  if (capacityEvidence.fixed != null) {
    return [
      {
        key: profile.profileId,
        label: 'Fixed capacity',
        repository: null,
        deficit: capacityEvidence.fixed,
      },
    ];
  }
  return capacityEvidence.targets.map((target: TargetCapacityDeficitEvidence) => ({
    key: target.key,
    label: target.key,
    repository: target.repository,
    deficit: target,
  }));
}
