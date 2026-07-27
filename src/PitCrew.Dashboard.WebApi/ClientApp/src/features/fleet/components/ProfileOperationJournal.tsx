import {
  describeJournalAvailability,
  describeManagerEvent,
  orderedManagerEvents,
  type ManagerObservedState,
} from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ProfileEvidenceDisclosure } from './ProfileEvidenceDisclosure';

/**
 * Renders the bounded chronological manager operation journal. Sequences are durable across manager
 * restart, so one event appears once regardless of how many heartbeats carried it.
 */
export function ProfileOperationJournal({ profile }: { readonly profile: ManagerObservedState }) {
  const journal = profile.operationJournal;
  const availability = describeJournalAvailability(journal);
  const events = orderedManagerEvents(journal);

  return (
    <ProfileEvidenceDisclosure
      title="Manager operations"
      description="Bounded chronology of manager-reported operations, newest first."
      summary={
        <>
          <span>
            {events.length} {events.length === 1 ? 'event' : 'events'}
          </span>
          <StatusBadge status={availability.status} />
        </>
      }
      testId={`profile-operations-${profile.profileId}`}
    >
      <p
        className="text-sm text-muted-foreground"
        data-testid={`profile-operations-availability-${profile.profileId}`}
      >
        {availability.description}
      </p>
      {events.length === 0 ? null : (
        <ol
          className="mt-3 grid gap-2"
          aria-label={`Manager operations for profile ${profile.profileId}, newest first`}
        >
          {events.map((event) => (
            <li
              key={event.sequence}
              className="grid gap-1 rounded-md border bg-background px-3 py-2"
              data-testid={`profile-operation-${profile.profileId}-${event.sequence}`}
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="min-w-0">
                  <span className="font-mono text-xs">{event.operation}</span>
                  <span className="text-xs text-muted-foreground"> · {event.subsystem}</span>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <StatusBadge status={event.outcome} />
                  <time className="text-xs text-muted-foreground" dateTime={event.observedAt}>
                    {formatTime(event.observedAt)}
                  </time>
                </div>
              </div>
              <p className="text-xs text-muted-foreground">{describeManagerEvent(event)}</p>
            </li>
          ))}
        </ol>
      )}
    </ProfileEvidenceDisclosure>
  );
}
