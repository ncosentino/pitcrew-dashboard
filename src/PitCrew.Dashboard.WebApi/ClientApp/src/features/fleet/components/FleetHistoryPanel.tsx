import { useState } from 'react';

import {
  buildDeficitReasonChanges,
  buildHistorySeries,
  describeDeficitEvidence,
  describeHistoryAvailability,
  describeHistoryJournal,
  describeManagerEvent,
  useFleetHistory,
  type ProfileHistory,
} from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { TimeSeriesChart } from '@/core/ui/TimeSeriesChart';

interface FleetHistoryPanelProps {
  readonly tenantId: string;
  readonly nodeId: string;
  readonly profileId: string | null;
  readonly testId: string;
}

interface HistoryRange {
  readonly hours: number;
  readonly label: string;
  readonly resolution: 'raw' | 'hourly';
  readonly pointLimit: number;
  readonly eventLimit: number;
  readonly description: string;
}

const scrollRegionClasses =
  'max-h-64 overflow-auto rounded focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-600';

const ranges: readonly HistoryRange[] = [
  {
    hours: 4,
    label: 'Last 4 hours (every observation)',
    resolution: 'raw',
    pointLimit: 1000,
    eventLimit: 200,
    description:
      'Showing up to 1000 retained per-observation samples per profile. At the usual heartbeat rate that covers roughly the last four hours; longer per-observation ranges cannot be shown truthfully because the response is capped.',
  },
  {
    hours: 24,
    label: 'Last 24 hours (hourly peaks)',
    resolution: 'hourly',
    pointLimit: 48,
    eventLimit: 200,
    description:
      'Showing deterministic hourly peaks aligned to whole UTC hours. Partial hours at either edge of the range are excluded.',
  },
  {
    hours: 168,
    label: 'Last 7 days (hourly peaks)',
    resolution: 'hourly',
    pointLimit: 200,
    eventLimit: 200,
    description:
      'Showing deterministic hourly peaks aligned to whole UTC hours. Partial hours at either edge of the range are excluded.',
  },
  {
    hours: 720,
    label: 'Last 30 days (hourly peaks)',
    resolution: 'hourly',
    pointLimit: 800,
    eventLimit: 200,
    description:
      'Showing deterministic hourly peaks aligned to whole UTC hours. Partial hours at either edge of the range are excluded.',
  },
];

function ProfileHistorySections({
  history,
  resolution,
}: {
  readonly history: ProfileHistory;
  readonly resolution: 'raw' | 'hourly';
}) {
  const availability = describeHistoryAvailability(history, resolution);
  const journal = describeHistoryJournal(history);
  const deficitEvidence = describeDeficitEvidence(history);
  const groups = buildHistorySeries(history, resolution);
  const deficits = buildDeficitReasonChanges(history);

  return (
    <div className="grid gap-4" data-testid={`history-profile-${history.profileId}`}>
      <div className="flex flex-wrap items-center gap-2">
        <StatusBadge status={availability.status} />
        <p className="text-xs text-muted-foreground">{availability.description}</p>
      </div>
      {availability.status === 'unavailable' ? null : (
        <div className="grid gap-6">
          {groups.map((group) => (
            <TimeSeriesChart
              description={group.description}
              headingLevel="h4"
              key={group.key}
              series={group.series}
              testId={`history-chart-${history.profileId}-${group.key}`}
              title={group.label}
              unit={group.unit}
            />
          ))}
        </div>
      )}

      <section className="grid gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <h4 className="text-sm font-semibold">Capacity-deficit reasons</h4>
          <StatusBadge status={deficitEvidence.status} />
          <p className="text-xs text-muted-foreground">{deficitEvidence.description}</p>
        </div>
        {deficits.length === 0 ? null : (
          <div
            aria-label={`Capacity-deficit reason changes for profile ${history.profileId}`}
            className={scrollRegionClasses}
            role="region"
            tabIndex={0}
          >
            <table
              className="w-full text-left text-xs"
              data-testid={`history-deficits-${history.profileId}`}
            >
              <caption className="sr-only">
                Manager capacity-deficit reason changes for profile {history.profileId}, for every
                autoscaling target.
              </caption>
              <thead className="text-muted-foreground">
                <tr>
                  <th scope="col">Observed at</th>
                  <th scope="col">Target</th>
                  <th scope="col">Reason</th>
                  <th scope="col">Freshness</th>
                  <th scope="col">Local shortfall</th>
                  <th scope="col">Eligibility shortfall</th>
                </tr>
              </thead>
              <tbody>
                {deficits.map((change) => (
                  <tr key={`${change.targetKey}-${change.at}`}>
                    <th className="font-normal" scope="row">
                      {formatTime(change.at)}
                    </th>
                    <td>{change.repository ?? change.targetKey}</td>
                    <td>{change.reason}</td>
                    <td>{change.freshness}</td>
                    <td>{change.localDeficit}</td>
                    <td>{change.eligibilityDeficit ?? 'Unavailable'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="grid gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <h4 className="text-sm font-semibold">Subsystem health changes</h4>
          <p className="text-xs text-muted-foreground">
            Only observations where manager-reported subsystem health changed are listed.
          </p>
        </div>
        {history.subsystemHealthChanges.length === 0 ? (
          <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
            No retained observation inside this range carried a manager subsystem health change.
          </p>
        ) : (
          <div
            aria-label={`Subsystem health changes for profile ${history.profileId}`}
            className={scrollRegionClasses}
            role="region"
            tabIndex={0}
          >
            <table
              className="w-full text-left text-xs"
              data-testid={`history-subsystems-${history.profileId}`}
            >
              <caption className="sr-only">
                Manager subsystem health changes for profile {history.profileId}.
              </caption>
              <thead className="text-muted-foreground">
                <tr>
                  <th scope="col">Observed at</th>
                  <th scope="col">Subsystem</th>
                  <th scope="col">State</th>
                  <th scope="col">Consecutive failures</th>
                  <th scope="col">Last failure</th>
                </tr>
              </thead>
              <tbody>
                {history.subsystemHealthChanges.map((change) => (
                  <tr key={`${change.subsystem}-${change.observedAt}`}>
                    <th className="font-normal" scope="row">
                      {formatTime(change.observedAt)}
                    </th>
                    <td>{change.subsystem}</td>
                    <td>{change.state}</td>
                    <td>{change.consecutiveFailures}</td>
                    <td>{change.lastFailureReason ?? 'Unreported'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="grid gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <h4 className="text-sm font-semibold">Manager operations</h4>
          <StatusBadge status={journal.status} />
          <p className="text-xs text-muted-foreground">{journal.description}</p>
        </div>
        {history.events.length === 0 ? (
          <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
            No durable manager operation was retained in this range.
          </p>
        ) : (
          <div
            aria-label={`Retained manager operations for profile ${history.profileId}`}
            className={scrollRegionClasses}
            role="region"
            tabIndex={0}
          >
            <ul className="space-y-1 text-xs" data-testid={`history-events-${history.profileId}`}>
              {history.events.map((event) => (
                <li
                  className="rounded border px-3 py-2"
                  key={`${event.managerInstanceId}-${event.sequence}-${event.observedAt}`}
                >
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-mono">#{event.sequence}</span>
                    <StatusBadge status={event.outcome} />
                    <span className="text-muted-foreground">{formatTime(event.observedAt)}</span>
                  </div>
                  <p className="mt-1 text-muted-foreground">{describeManagerEvent(event)}</p>
                </li>
              ))}
            </ul>
          </div>
        )}
      </section>
    </div>
  );
}

function ProfileHistoryDisclosure({
  history,
  resolution,
  isInitiallyOpen,
}: {
  readonly history: ProfileHistory;
  readonly resolution: 'raw' | 'hourly';
  readonly isInitiallyOpen: boolean;
}) {
  const [isOpen, setIsOpen] = useState(isInitiallyOpen);

  return (
    <details
      className="group rounded border"
      data-testid={`history-disclosure-${history.profileId}`}
      onToggle={(event) => setIsOpen(event.currentTarget.open)}
      open={isOpen}
    >
      <summary className="flex cursor-pointer list-none flex-wrap items-center gap-2 px-3 py-2 [&::-webkit-details-marker]:hidden">
        <h3 className="text-sm font-semibold">{history.profileId}</h3>
        <span className="text-xs text-muted-foreground group-open:hidden">
          Show profile history
        </span>
        <span className="hidden text-xs text-muted-foreground group-open:inline">
          Hide profile history
        </span>
      </summary>
      <div className="border-t px-3 py-3">
        {isOpen ? <ProfileHistorySections history={history} resolution={resolution} /> : null}
      </div>
    </details>
  );
}

/**
 * Renders bounded retained history for one node or one profile.
 *
 * The same panel serves the node and profile views so the accessible chart, deficit, and manager
 * operation presentation is defined once. Every profile is grouped behind its own disclosure so a
 * node with many profiles does not render an unbounded wall of charts.
 */
export function FleetHistoryPanel({ tenantId, nodeId, profileId, testId }: FleetHistoryPanelProps) {
  const [rangeHours, setRangeHours] = useState(ranges[0].hours);
  const [isOpen, setIsOpen] = useState(false);
  const range = ranges.find((candidate) => candidate.hours === rangeHours) ?? ranges[0];
  const { history, error, isLoading, isStale } = useFleetHistory({
    tenantId,
    nodeId,
    profileId,
    rangeHours: range.hours,
    resolution: range.resolution,
    pointLimit: range.pointLimit,
    eventLimit: range.eventLimit,
    enabled: isOpen,
  });

  return (
    <details
      className="group border-b bg-muted/5"
      data-testid={testId}
      onToggle={(event) => setIsOpen(event.currentTarget.open)}
    >
      <summary className="flex cursor-pointer list-none flex-col items-stretch gap-2 px-4 py-3 sm:flex-row sm:items-center sm:justify-between sm:gap-3 [&::-webkit-details-marker]:hidden">
        <div className="min-w-0">
          <h2 className="font-semibold">History</h2>
          <p className="text-xs text-muted-foreground">
            Bounded retained telemetry and durable manager operations. History is retained only
            while a connector reports advancing manager observations.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground sm:justify-end">
          <span className="group-open:hidden">Show history</span>
          <span className="hidden group-open:inline">Hide history</span>
        </div>
      </summary>
      <div className="grid gap-4 border-t px-4 py-4">
        <div className="flex flex-wrap items-center gap-2">
          <label className="text-xs font-medium" htmlFor={`${testId}-range`}>
            Time range
          </label>
          <select
            className="rounded border bg-background px-2 py-1 text-xs"
            id={`${testId}-range`}
            onChange={(event) => setRangeHours(Number(event.currentTarget.value))}
            value={String(range.hours)}
          >
            {ranges.map((candidate) => (
              <option key={candidate.hours} value={String(candidate.hours)}>
                {candidate.label}
              </option>
            ))}
          </select>
          <span className="text-xs text-muted-foreground">{range.description}</span>
        </div>
        {isLoading ? (
          <p className="text-xs text-muted-foreground" role="status">
            {isStale
              ? 'Loading the selected range; showing the previous range…'
              : 'Loading history…'}
          </p>
        ) : null}
        {error != null ? (
          <p
            className="rounded border border-red-300 bg-red-50 px-3 py-2 text-xs text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
            role="alert"
          >
            {error}
          </p>
        ) : null}
        {history != null && (history.pointsTruncated || history.eventsTruncated) ? (
          <p
            className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
            role="status"
          >
            This node response reached its overall limits of {history.pointLimit} points and{' '}
            {history.eventLimit} events across all profiles. The most recent data inside the range
            is shown and older data inside the same range is hidden. Open a single profile or narrow
            the range to see the hidden observations.
          </p>
        ) : null}
        {history != null && history.profiles.length === 0 ? (
          <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
            No retained history exists for this range.
          </p>
        ) : null}
        {history?.profiles.map((profile) => (
          <ProfileHistoryDisclosure
            history={profile}
            isInitiallyOpen={history.profiles.length === 1}
            key={profile.profileId}
            resolution={history.resolution}
          />
        ))}
      </div>
    </details>
  );
}
