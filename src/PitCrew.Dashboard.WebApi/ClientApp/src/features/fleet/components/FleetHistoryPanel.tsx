import { useState } from 'react';

import {
  buildDeficitReasonChanges,
  buildHistoryPresets,
  buildHistorySeries,
  describeDeficitEvidence,
  describeHistoryAvailability,
  describeHistoryJournal,
  describeIncompletenessFloor,
  describeManagerEvent,
  describeSubsystemHealthEvidence,
  describeWorkerUpdateEvidence,
  resolveCadenceMilliseconds,
  useFleetHistory,
  useHistoryCapabilities,
  type HistoryAvailability,
  type NodeHistoryResponse,
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
  readonly presentation?: 'disclosure' | 'page';
}

const scrollRegionClasses =
  'max-h-64 overflow-auto rounded focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-600';

/**
 * Renders one availability verdict as a badge, an explicit label, and its description.
 *
 * The badge alone only carries the coarse status, so an expired or truncated range would otherwise
 * be indistinguishable from an ordinary partial one. The label states the verdict in the words the
 * describing function chose, which is the only place `Expired` is ever said out loud.
 */
function AvailabilityNote({ availability }: { readonly availability: HistoryAvailability }) {
  return (
    <>
      <StatusBadge status={availability.status} />
      <span className="text-xs font-medium">{availability.label}</span>
      <p className="text-xs text-muted-foreground">{availability.description}</p>
    </>
  );
}

function ProfileHistorySections({
  history,
  resolution,
  expectedRawCadenceSeconds,
}: {
  readonly history: ProfileHistory;
  readonly resolution: 'raw' | 'hourly';
  readonly expectedRawCadenceSeconds: number | null;
}) {
  const availability = describeHistoryAvailability(history, resolution);
  const journal = describeHistoryJournal(history);
  const deficitEvidence = describeDeficitEvidence(history);
  const subsystemEvidence = describeSubsystemHealthEvidence(history);
  const workerUpdateEvidence = describeWorkerUpdateEvidence(history);
  const groups = buildHistorySeries(history, resolution);
  const cadenceMilliseconds = resolveCadenceMilliseconds(
    history,
    resolution,
    expectedRawCadenceSeconds,
  );
  const deficits = buildDeficitReasonChanges(history);

  return (
    <div className="grid gap-4" data-testid={`history-profile-${history.profileId}`}>
      <div className="flex flex-wrap items-center gap-2">
        <AvailabilityNote availability={availability} />
      </div>
      {availability.status === 'unavailable' ? null : (
        <div className="grid gap-6">
          {groups.map((group) => (
            <TimeSeriesChart
              cadenceMilliseconds={cadenceMilliseconds}
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
          <h4 className="text-sm font-semibold">Worker image rollout changes</h4>
          <AvailabilityNote availability={workerUpdateEvidence} />
        </div>
        {history.workerUpdateChanges.length === 0 ? (
          <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
            {workerUpdateEvidence.description}
          </p>
        ) : (
          <div
            aria-label={`Worker image rollout changes for profile ${history.profileId}`}
            className={scrollRegionClasses}
            role="region"
            tabIndex={0}
          >
            <table
              className="w-full text-left text-xs"
              data-testid={`history-worker-updates-${history.profileId}`}
            >
              <caption className="sr-only">
                Worker image rollout changes for profile {history.profileId}.
              </caption>
              <thead className="text-muted-foreground">
                <tr>
                  <th scope="col">Observed at</th>
                  <th scope="col">Change</th>
                  <th scope="col">Status</th>
                  <th scope="col">Target image</th>
                  <th scope="col">Current</th>
                  <th scope="col">Stale</th>
                  <th scope="col">Error</th>
                </tr>
              </thead>
              <tbody>
                {history.workerUpdateChanges.map((change) => (
                  <tr key={`${change.kind}-${change.observedAt}`}>
                    <th className="font-normal" scope="row">
                      {formatTime(change.observedAt)}
                    </th>
                    <td>{change.kind.replaceAll('-', ' ')}</td>
                    <td>
                      <StatusBadge status={change.status} />
                    </td>
                    <td className="font-mono" title={change.targetImage ?? change.targetRevision}>
                      {change.targetImage ?? change.targetRevision.slice(0, 12)}
                    </td>
                    <td>{change.currentWorkers}</td>
                    <td>{change.staleWorkers}</td>
                    <td>{change.lastError ?? 'None'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="grid gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <h4 className="text-sm font-semibold">Capacity-deficit reasons</h4>
          <AvailabilityNote availability={deficitEvidence} />
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
          <AvailabilityNote availability={subsystemEvidence} />
        </div>
        {history.subsystemHealthChanges.length === 0 ? (
          <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
            {subsystemEvidence.description}
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
          <AvailabilityNote availability={journal} />
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
  expectedRawCadenceSeconds,
}: {
  readonly history: ProfileHistory;
  readonly resolution: 'raw' | 'hourly';
  readonly isInitiallyOpen: boolean;
  readonly expectedRawCadenceSeconds: number | null;
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
        {isOpen ? (
          <ProfileHistorySections
            expectedRawCadenceSeconds={expectedRawCadenceSeconds}
            history={history}
            resolution={resolution}
          />
        ) : null}
      </div>
    </details>
  );
}

function describeResponseTruncation(history: NodeHistoryResponse | null): string | null {
  if (history == null) return null;
  const capped: string[] = [];
  if (history.pointsTruncated) {
    capped.push(
      `points (${history.profilePointLimit} per profile, ${history.nodePointLimit} across all profiles)`,
    );
  }
  if (history.eventsTruncated) {
    capped.push(
      `manager operations (${history.profileEventLimit} per profile, ${history.nodeEventLimit} across all profiles)`,
    );
  }
  if (history.diagnosticsTruncated) {
    capped.push(
      `diagnostics (${history.profileSubsystemHealthLimit} subsystem-health, ${history.profileCapacityDeficitLimit} capacity-deficit, and ${history.profileWorkerUpdateLimit} worker-rollout rows per profile, ${history.nodeDiagnosticLimit} combined across all profiles)`,
    );
  }
  if (capped.length === 0) return null;
  return `This response reached its limits for ${capped.join(', ')}. The most recent data inside the range is shown and older retained data inside the same range is hidden. Open a single profile or narrow the range to see the hidden observations.`;
}

/**
 * Composes the single assertive-free status announcement for the panel.
 *
 * Loading, showing a stale range, and reaching a response limit are separate facts that can all be
 * true at once. They are merged into one message inside one live region so a screen reader is never
 * given two simultaneous status announcements that race each other.
 */
function describeLiveState({
  isBusy,
  isStale,
  truncation,
}: {
  readonly isBusy: boolean;
  readonly isStale: boolean;
  readonly truncation: string | null;
}): string | null {
  const parts: string[] = [];
  if (isBusy) {
    parts.push(
      isStale ? 'Loading the selected range; showing the previous range…' : 'Loading history…',
    );
  }
  if (truncation != null) {
    parts.push(truncation);
  }
  return parts.length === 0 ? null : parts.join(' ');
}

/**
 * Renders bounded retained history for one node or one profile.
 *
 * The same panel serves node and profile routes so chart, deficit, and manager-operation semantics
 * stay consistent. Node history groups profiles behind disclosures; a profile history route renders
 * its one selected profile directly.
 */
export function FleetHistoryPanel({
  tenantId,
  nodeId,
  profileId,
  testId,
  presentation = 'disclosure',
}: FleetHistoryPanelProps) {
  const [presetKey, setPresetKey] = useState<string | null>(null);
  const [isOpen, setIsOpen] = useState(presentation === 'page');
  const {
    capabilities,
    error: capabilitiesError,
    isLoading: isLoadingCapabilities,
  } = useHistoryCapabilities(tenantId, isOpen);
  const presets = capabilities == null ? [] : buildHistoryPresets(capabilities);
  const range = presets.find((candidate) => candidate.key === presetKey) ?? presets[0] ?? null;
  const { history, error, isLoading, isStale } = useFleetHistory({
    tenantId,
    nodeId,
    profileId,
    rangeHours: range?.hours ?? 0,
    resolution: range?.resolution ?? 'raw',
    pointLimit: range?.pointLimit ?? null,
    eventLimit: range?.eventLimit ?? null,
    diagnosticLimit: range?.diagnosticLimit ?? null,
    enabled: isOpen && range != null,
  });
  const isBusy = isLoadingCapabilities || isLoading;
  const liveMessage = describeLiveState({
    isBusy,
    isStale,
    truncation: isBusy ? null : describeResponseTruncation(history),
  });
  const content = (
    <>
      <div className="flex flex-wrap items-center gap-2">
        <label className="text-xs font-medium" htmlFor={`${testId}-range`}>
          Time range
        </label>
        <select
          className="rounded border bg-background px-2 py-1 text-xs"
          disabled={presets.length === 0}
          id={`${testId}-range`}
          onChange={(event) => setPresetKey(event.currentTarget.value)}
          value={range?.key ?? ''}
        >
          {presets.map((candidate) => (
            <option key={candidate.key} value={candidate.key}>
              {candidate.label}
            </option>
          ))}
        </select>
        <span className="text-xs text-muted-foreground">{range?.description ?? ''}</span>
      </div>
      <p
        aria-live="polite"
        className={
          liveMessage == null
            ? 'sr-only'
            : isLoading
              ? 'text-xs text-muted-foreground'
              : 'rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100'
        }
        role="status"
      >
        {liveMessage ?? ''}
      </p>
      {capabilitiesError != null ? (
        <p
          className="rounded border border-red-300 bg-red-50 px-3 py-2 text-xs text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
          role="alert"
        >
          {capabilitiesError}
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
      {history?.incompletenessFloors.map((floor) => (
        <p
          className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
          key={floor.scope}
        >
          {describeIncompletenessFloor(floor)}
        </p>
      ))}
      {history != null && history.profiles.length === 0 ? (
        <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
          No retained history exists for this range.
        </p>
      ) : null}
      {history?.profiles.map((profile) =>
        presentation === 'page' && profileId !== null ? (
          <ProfileHistorySections
            expectedRawCadenceSeconds={capabilities?.expectedRawCadenceSeconds ?? null}
            history={profile}
            key={profile.profileId}
            resolution={history.resolution}
          />
        ) : (
          <ProfileHistoryDisclosure
            history={profile}
            expectedRawCadenceSeconds={capabilities?.expectedRawCadenceSeconds ?? null}
            isInitiallyOpen={history.profiles.length === 1}
            key={profile.profileId}
            resolution={history.resolution}
          />
        ),
      )}
    </>
  );

  if (presentation === 'page') {
    return (
      <section className="overflow-hidden rounded-lg border bg-card shadow-sm" data-testid={testId}>
        <div className="px-4 py-4">
          <h2 className="font-semibold">History</h2>
          <p className="text-xs text-muted-foreground">
            Bounded retained telemetry and durable manager operations. History is retained only
            while a connector reports advancing manager observations.
          </p>
        </div>
        <div className="grid gap-4 border-t px-4 py-4">{content}</div>
      </section>
    );
  }

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
      <div className="grid gap-4 border-t px-4 py-4">{content}</div>
    </details>
  );
}
