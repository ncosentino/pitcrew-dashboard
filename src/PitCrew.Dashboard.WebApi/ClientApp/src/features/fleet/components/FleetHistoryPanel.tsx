import { useState } from 'react';

import {
  buildDeficitReasonChanges,
  buildHistorySeries,
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

const ranges = [
  { hours: 6, label: 'Last 6 hours', resolution: 'raw' as const },
  { hours: 24, label: 'Last 24 hours', resolution: 'raw' as const },
  { hours: 168, label: 'Last 7 days', resolution: 'hourly' as const },
  { hours: 720, label: 'Last 30 days', resolution: 'hourly' as const },
];

function ProfileHistoryCharts({
  history,
  resolution,
}: {
  readonly history: ProfileHistory;
  readonly resolution: 'raw' | 'hourly';
}) {
  const availability = describeHistoryAvailability(history, resolution);
  const journal = describeHistoryJournal(history);
  const groups = buildHistorySeries(history, resolution);
  const deficits = buildDeficitReasonChanges(history);

  return (
    <section className="grid gap-4" data-testid={`history-profile-${history.profileId}`}>
      <div className="flex flex-wrap items-center gap-2">
        <h3 className="font-semibold">{history.profileId}</h3>
        <StatusBadge status={availability.status} />
        <p className="text-xs text-muted-foreground">{availability.description}</p>
      </div>
      {availability.status === 'unavailable' ? null : (
        <div className="grid gap-6">
          {groups.map((group) => (
            <TimeSeriesChart
              description={group.description}
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
          <h3 className="text-sm font-semibold">Capacity-deficit reasons</h3>
          <p className="text-xs text-muted-foreground">
            Only observations where manager-reported deficit evidence changed are listed.
          </p>
        </div>
        {deficits.length === 0 ? (
          <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
            No retained observation carried manager capacity-deficit evidence in this range.
          </p>
        ) : (
          <div className="max-h-64 overflow-auto">
            <table
              className="w-full text-left text-xs"
              data-testid={`history-deficits-${history.profileId}`}
            >
              <caption className="sr-only">
                Manager capacity-deficit reason changes for profile {history.profileId}.
              </caption>
              <thead className="text-muted-foreground">
                <tr>
                  <th scope="col">Observed at</th>
                  <th scope="col">Reason</th>
                  <th scope="col">Freshness</th>
                  <th scope="col">Local shortfall</th>
                  <th scope="col">Eligibility shortfall</th>
                </tr>
              </thead>
              <tbody>
                {deficits.map((change) => (
                  <tr key={change.at}>
                    <th className="font-normal" scope="row">
                      {formatTime(change.at)}
                    </th>
                    <td>{change.reason ?? 'Unreported'}</td>
                    <td>{change.freshness ?? 'Unreported'}</td>
                    <td>{change.localDeficit ?? 'Unavailable'}</td>
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
          <h3 className="text-sm font-semibold">Manager operations</h3>
          <StatusBadge status={journal.status} />
          <p className="text-xs text-muted-foreground">{journal.description}</p>
        </div>
        {history.events.length === 0 ? (
          <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
            No durable manager operation was retained in this range.
          </p>
        ) : (
          <ul
            className="max-h-64 space-y-1 overflow-auto text-xs"
            data-testid={`history-events-${history.profileId}`}
          >
            {history.events.map((event) => (
              <li
                className="rounded border px-3 py-2"
                key={`${event.managerInstanceId}-${event.sequence}`}
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
        )}
      </section>
    </section>
  );
}

/**
 * Renders bounded retained history for one node or one profile.
 *
 * The same panel serves the node and profile views so the accessible chart, deficit, and manager
 * operation presentation is defined once.
 */
export function FleetHistoryPanel({ tenantId, nodeId, profileId, testId }: FleetHistoryPanelProps) {
  const [rangeHours, setRangeHours] = useState(24);
  const [isOpen, setIsOpen] = useState(false);
  const range = ranges.find((candidate) => candidate.hours === rangeHours) ?? ranges[1];
  const { history, error, isLoading } = useFleetHistory({
    tenantId,
    nodeId,
    profileId,
    rangeHours: range.hours,
    resolution: range.resolution,
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
          <span className="text-xs text-muted-foreground">
            {range.resolution === 'hourly'
              ? 'Showing deterministic hourly rollups.'
              : 'Showing retained per-observation samples.'}
          </span>
        </div>
        {isLoading ? (
          <p className="text-xs text-muted-foreground" role="status">
            Loading history…
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
        {history != null && history.profiles.length === 0 ? (
          <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
            No retained history exists for this range.
          </p>
        ) : null}
        {history?.profiles.map((profile) => (
          <ProfileHistoryCharts
            history={profile}
            key={profile.profileId}
            resolution={history.resolution}
          />
        ))}
      </div>
    </details>
  );
}
