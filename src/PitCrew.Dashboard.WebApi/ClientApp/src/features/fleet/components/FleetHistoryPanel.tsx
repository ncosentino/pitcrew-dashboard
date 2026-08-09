import { useState } from 'react';

import {
  buildDeficitReasonChanges,
  buildHostAdmissionChanges,
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
  type RunnerAssignmentInterval,
} from '@/core/fleet';
import { formatCounter, formatTime } from '@/core/formatting/formatters';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { TimeSeriesChart, type TimeSeriesInterval } from '@/core/ui/TimeSeriesChart';

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
  runnerAssignments,
}: {
  readonly history: ProfileHistory;
  readonly resolution: 'raw' | 'hourly';
  readonly expectedRawCadenceSeconds: number | null;
  readonly runnerAssignments: ReadonlyArray<RunnerAssignmentInterval>;
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
  const hostAdmissionChanges = buildHostAdmissionChanges(history);
  const workloadIntervals = runnerAssignments.flatMap((assignment): TimeSeriesInterval[] => {
    const job = assignment.job;
    if (job == null) return [];
    return [
      {
        key: `${assignment.runnerNameHash}:${job.jobId}`,
        label: job.displayName ?? `GitHub job ${job.jobId}`,
        from: job.startedAt,
        to: job.finishedAt ?? assignment.lastObservedAt,
        href: `${job.repository}/actions/runs/${job.workflowRunId}/job/${job.jobId}`,
      },
    ];
  });

  return (
    <div className="grid gap-4" data-testid={`history-profile-${history.profileId}`}>
      <div className="flex flex-wrap items-center gap-2">
        <AvailabilityNote availability={availability} />
      </div>
      {workloadIntervals.length > 0 ? (
        <section className="grid gap-2" data-testid={`history-workloads-${history.profileId}`}>
          <h4 className="text-sm font-semibold">Retained workload intervals</h4>
          <div
            aria-label={`Retained workload intervals for profile ${history.profileId}`}
            className={scrollRegionClasses}
            role="region"
            tabIndex={0}
          >
            <table className="w-full text-left text-xs">
              <thead className="text-muted-foreground">
                <tr>
                  <th scope="col">Workload</th>
                  <th scope="col">Started</th>
                  <th scope="col">Finished / last observed</th>
                </tr>
              </thead>
              <tbody>
                {workloadIntervals.map((interval) => (
                  <tr className="border-t" key={interval.key}>
                    <th className="font-normal" scope="row">
                      {interval.href ? (
                        <a
                          className="font-medium text-link underline-offset-4 hover:underline"
                          href={interval.href}
                          rel="noreferrer"
                          target="_blank"
                        >
                          {interval.label}
                        </a>
                      ) : (
                        interval.label
                      )}
                    </th>
                    <td>{formatTime(interval.from)}</td>
                    <td>{formatTime(interval.to)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}
      {availability.status === 'unavailable' ? null : (
        <div className="grid gap-6">
          {groups.map((group) => (
            <TimeSeriesChart
              cadenceMilliseconds={cadenceMilliseconds}
              description={group.description}
              headingLevel="h4"
              intervals={workloadIntervals}
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
          <h4 className="text-sm font-semibold">Host admission changes</h4>
          {resolution === 'hourly' ? <StatusBadge status="unavailable" /> : null}
        </div>
        {resolution === 'hourly' ? (
          <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
            Host-admission status and reservation policy are categorical evidence and are not
            synthesized into hourly extrema. Select raw resolution to inspect retained changes.
          </p>
        ) : hostAdmissionChanges.length === 0 ? (
          <p className="rounded border border-dashed px-3 py-3 text-xs text-muted-foreground">
            No host-admission evidence was retained in this range. Older manager observations remain
            readable, and missing evidence is unavailable rather than zero.
          </p>
        ) : (
          <div
            aria-label={`Host admission changes for profile ${history.profileId}`}
            className={scrollRegionClasses}
            role="region"
            tabIndex={0}
          >
            <table
              className="w-full text-left text-xs"
              data-testid={`history-host-admission-${history.profileId}`}
            >
              <caption className="sr-only">
                Raw retained host-admission changes for profile {history.profileId}.
              </caption>
              <thead className="text-muted-foreground">
                <tr>
                  <th scope="col">Observed at</th>
                  <th scope="col">State</th>
                  <th scope="col">Available</th>
                  <th scope="col">Held</th>
                  <th scope="col">Reserved</th>
                  <th scope="col">Borrowed</th>
                  <th scope="col">Withheld</th>
                  <th scope="col">Reservation</th>
                  <th scope="col">Epoch / sequence</th>
                </tr>
              </thead>
              <tbody>
                {hostAdmissionChanges.map((change) => (
                  <tr key={`${change.observedAt}-${change.epoch}-${change.decisionSequence}`}>
                    <th className="font-normal" scope="row">
                      {formatTime(change.observedAt)}
                    </th>
                    <td>
                      <StatusBadge status={change.status} />
                    </td>
                    <td>{formatCounter(change.availableUnits)}</td>
                    <td>{formatCounter(change.heldUnits)}</td>
                    <td>{formatCounter(change.reservedUnits)}</td>
                    <td>{formatCounter(change.borrowedUnits)}</td>
                    <td>{formatCounter(change.withheldUnits)}</td>
                    <td>
                      {change.borrowable == null
                        ? 'Unavailable'
                        : change.borrowable
                          ? 'Borrowable'
                          : 'Protected'}
                    </td>
                    <td className="tabular-nums">
                      {formatCounter(change.epoch)} / {formatCounter(change.decisionSequence)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

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
  runnerAssignments,
}: {
  readonly history: ProfileHistory;
  readonly resolution: 'raw' | 'hourly';
  readonly isInitiallyOpen: boolean;
  readonly expectedRawCadenceSeconds: number | null;
  readonly runnerAssignments: ReadonlyArray<RunnerAssignmentInterval>;
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
            runnerAssignments={runnerAssignments}
          />
        ) : null}
      </div>
    </details>
  );
}

function describeResponseTruncation(history: NodeHistoryResponse | null): string | null {
  if (history == null) return null;
  const capped: string[] = [];
  let hasProfileScopedTruncation = false;
  if (history.pointsTruncated) {
    hasProfileScopedTruncation = true;
    capped.push(
      `points (${history.profilePointLimit} per profile, ${history.nodePointLimit} across all profiles)`,
    );
  }
  if (history.eventsTruncated) {
    hasProfileScopedTruncation = true;
    capped.push(
      `manager operations (${history.profileEventLimit} per profile, ${history.nodeEventLimit} across all profiles)`,
    );
  }
  if (history.diagnosticsTruncated) {
    hasProfileScopedTruncation = true;
    capped.push(
      `diagnostics (${history.profileSubsystemHealthLimit} subsystem-health, ${history.profileCapacityDeficitLimit} capacity-deficit, and ${history.profileWorkerUpdateLimit} worker-rollout rows per profile, ${history.nodeDiagnosticLimit} combined across all profiles)`,
    );
  }
  if (history.hardwareRevisionsTruncated) {
    capped.push(`hardware revisions (${history.nodeDiagnosticLimit} across the node)`);
  }
  if (history.runnerAssignmentsTruncated) {
    capped.push(`runner and workload intervals (${history.nodeDiagnosticLimit} across the node)`);
  }
  if (capped.length === 0) return null;
  return `This response reached its limits for ${capped.join(', ')}. The most recent data inside the range is shown and older retained data inside the same range is hidden. Narrow the range to see the hidden observations.${hasProfileScopedTruncation ? ' Opening a single profile can also recover profile-scoped rows.' : ''}`;
}

function HostHardwareRevisionHistory({ history }: { readonly history: NodeHistoryResponse }) {
  if (history.hardwareRevisions.length === 0) return null;
  return (
    <section className="grid gap-2" data-testid="history-hardware-revisions">
      <div className="flex flex-wrap items-center gap-2">
        <h3 className="text-sm font-semibold">Host hardware changes</h3>
        {history.hardwareRevisions.length > 1 ? (
          <StatusBadge status="changed" />
        ) : (
          <StatusBadge status="current" />
        )}
      </div>
      <p className="text-xs text-muted-foreground">
        Each row is one observed inventory episode. Consecutive reports from several profiles are
        deduplicated.
      </p>
      <div
        aria-label="Host hardware revisions"
        className={scrollRegionClasses}
        role="region"
        tabIndex={0}
      >
        <table className="w-full text-left text-xs">
          <caption className="sr-only">Host hardware changes inside the requested range.</caption>
          <thead className="text-muted-foreground">
            <tr>
              <th scope="col">First observed</th>
              <th scope="col">Processor</th>
              <th scope="col">Physical / logical</th>
              <th scope="col">Docker runtime</th>
              <th scope="col">Source profile</th>
              <th scope="col">Inventory</th>
            </tr>
          </thead>
          <tbody>
            {history.hardwareRevisions.map((revision) => (
              <tr
                className="border-t"
                key={`${revision.inventoryHash}:${revision.firstObservedAt}`}
              >
                <th className="font-normal" scope="row">
                  {formatTime(revision.firstObservedAt)}
                </th>
                <td>{revision.hardware.processorModel ?? 'Unavailable'}</td>
                <td>
                  {revision.hardware.physicalCoreCount ?? 'Unknown'} /{' '}
                  {revision.hardware.logicalProcessorCount ?? 'Unknown'}
                </td>
                <td>
                  {[revision.hardware.dockerServerVersion, revision.hardware.dockerStorageDriver]
                    .filter((value) => value != null)
                    .join(' / ') || 'Unavailable'}
                </td>
                <td>{revision.sourceProfileId}</td>
                <td className="font-mono">{revision.inventoryHash.slice(0, 12)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
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
      {liveMessage == null ? (
        <p aria-live="polite" className="sr-only" role="status"></p>
      ) : isLoading ? (
        <p aria-live="polite" className="text-xs text-muted-foreground" role="status">
          {liveMessage}
        </p>
      ) : (
        <StateBanner className="px-3 py-2 text-xs" tone="caution">
          {liveMessage}
        </StateBanner>
      )}
      {capabilitiesError != null ? (
        <StateBanner className="px-3 py-2 text-xs" role="alert" tone="critical">
          {capabilitiesError}
        </StateBanner>
      ) : null}
      {error != null ? (
        <StateBanner className="px-3 py-2 text-xs" role="alert" tone="critical">
          {error}
        </StateBanner>
      ) : null}
      {history?.incompletenessFloors.map((floor) => (
        <StateBanner className="px-3 py-2 text-xs" key={floor.scope} tone="caution">
          {describeIncompletenessFloor(floor)}
        </StateBanner>
      ))}
      {history == null ? null : <HostHardwareRevisionHistory history={history} />}
      {history != null &&
      history.profiles.length === 0 &&
      history.hardwareRevisions.length === 0 ? (
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
            runnerAssignments={history.runnerAssignments.filter(
              (assignment) => assignment.profileId === profile.profileId,
            )}
          />
        ) : (
          <ProfileHistoryDisclosure
            history={profile}
            expectedRawCadenceSeconds={capabilities?.expectedRawCadenceSeconds ?? null}
            isInitiallyOpen={history.profiles.length === 1}
            key={profile.profileId}
            resolution={history.resolution}
            runnerAssignments={history.runnerAssignments.filter(
              (assignment) => assignment.profileId === profile.profileId,
            )}
          />
        ),
      )}
    </>
  );

  if (presentation === 'page') {
    return (
      <section
        className="overflow-hidden rounded-lg border bg-card shadow-raised-surface"
        data-testid={testId}
      >
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
