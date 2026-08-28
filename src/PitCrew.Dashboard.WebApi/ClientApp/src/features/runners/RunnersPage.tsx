import { useCallback, useEffect, useMemo, useRef } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { useFleet } from '@/core/fleet';
import {
  formatBytes,
  formatCpuCores,
  formatOptionalBytes,
  formatPids,
  formatTime,
} from '@/core/formatting/formatters';
import { FilterChips, type FilterChipDescriptor } from '@/core/ui/FilterChips';
import { EmptyState } from '@/core/ui/EmptyState';
import { FilterToolbar } from '@/core/ui/FilterToolbar';
import { FormField } from '@/core/ui/FormField';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalTable, type OperationalTableColumn } from '@/core/ui/OperationalTable';
import { ReadinessSummary } from '@/core/ui/ReadinessSummary';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { WorkerExitEvidence, WorkerImageIdentity } from '@/core/ui/WorkerEvidenceCells';

import { RunnerDetail } from './RunnerDetail';
import {
  flattenFleetSlots,
  runnerAttentionRank,
  runnerNeedsReview,
  runnerSelectionId,
  type FleetSlot,
} from './runnerRows';

interface RunnersPageProps {
  readonly tenantId: string;
}

type SortKey =
  | 'attention'
  | 'node'
  | 'profile'
  | 'slot'
  | 'repository'
  | 'target'
  | 'activity'
  | 'registration'
  | 'state'
  | 'failures'
  | 'cpu'
  | 'memory'
  | 'pids'
  | 'network'
  | 'block'
  | 'image'
  | 'exit';
type SortDirection = 'asc' | 'desc';

const sortKeys = new Set<SortKey>([
  'attention',
  'node',
  'profile',
  'slot',
  'repository',
  'target',
  'activity',
  'registration',
  'state',
  'failures',
  'cpu',
  'memory',
  'pids',
  'network',
  'block',
  'image',
  'exit',
]);

const inputClassName = 'h-9 min-w-0 w-full rounded-md border bg-background px-3 text-sm';
const runnerMobileLimit = 50;
const tableColumns: ReadonlyArray<OperationalTableColumn> = [
  { key: 'runner', header: 'Runner' },
  { key: 'workload', header: 'Workload' },
  { key: 'state', header: 'State' },
  { key: 'resources', header: 'Resources' },
  { key: 'evidence', header: 'Evidence' },
];
const sortLabels: Record<SortKey, string> = {
  attention: 'Attention',
  node: 'Node',
  profile: 'Profile',
  slot: 'Slot',
  repository: 'Repository',
  target: 'Target',
  activity: 'Activity',
  registration: 'GitHub registration',
  state: 'Lifecycle state',
  failures: 'Failure count',
  cpu: 'CPU',
  memory: 'Memory',
  pids: 'PIDs',
  network: 'Network I/O',
  block: 'Block I/O',
  image: 'Worker image',
  exit: 'Last exit',
};

function compareText(left: string | null | undefined, right: string | null | undefined): number {
  if (left == null && right == null) return 0;
  if (left == null) return 1;
  if (right == null) return -1;
  const normalizedLeft = left.toLocaleLowerCase();
  const normalizedRight = right.toLocaleLowerCase();
  return normalizedLeft < normalizedRight ? -1 : normalizedLeft > normalizedRight ? 1 : 0;
}

function sortValue(row: FleetSlot, key: SortKey): string | number | null | undefined {
  switch (key) {
    case 'attention':
      return runnerAttentionRank(row);
    case 'node':
      return row.nodeName;
    case 'profile':
      return row.profileId;
    case 'slot':
      return row.slot.key;
    case 'repository':
      return row.slot.repository;
    case 'target':
      return row.slot.target;
    case 'activity':
      return row.slot.activity;
    case 'registration':
      return row.slot.registrationStatus ?? 'unknown';
    case 'state':
      return row.slot.state;
    case 'failures':
      return row.slot.failureCount;
    case 'cpu':
      return row.slot.resources?.cpuCores;
    case 'memory':
      return row.slot.resources?.memoryWorkingSetBytes;
    case 'pids':
      return row.slot.resources?.pids;
    case 'network':
      return sumCounters(row.slot.resources?.networkRxBytes, row.slot.resources?.networkTxBytes);
    case 'block':
      return sumCounters(row.slot.resources?.blockReadBytes, row.slot.resources?.blockWriteBytes);
    case 'image':
      return row.slot.imageId;
    case 'exit':
      return row.slot.lastExit?.classification;
  }
}

function sumCounters(
  left: number | null | undefined,
  right: number | null | undefined,
): number | null {
  if (left == null && right == null) return null;
  return (left ?? 0) + (right ?? 0);
}

function compareRows(left: FleetSlot, right: FleetSlot, key: SortKey): number {
  const leftValue = sortValue(left, key);
  const rightValue = sortValue(right, key);
  let comparison: number;
  if (typeof leftValue === 'number' && typeof rightValue === 'number') {
    comparison = leftValue - rightValue;
  } else {
    comparison = compareText(
      typeof leftValue === 'string' ? leftValue : null,
      typeof rightValue === 'string' ? rightValue : null,
    );
  }
  if (comparison !== 0) return comparison;
  return (
    compareText(left.nodeName, right.nodeName) ||
    compareText(left.nodeId, right.nodeId) ||
    compareText(left.profileId, right.profileId) ||
    compareText(left.slot.key, right.slot.key)
  );
}

function uniqueSorted(values: ReadonlyArray<string>): ReadonlyArray<string> {
  return [...new Set(values)].sort(compareText);
}

function formatFilterValue(value: string): string {
  return value.replaceAll('-', ' ');
}

function ResourceNotice({ rows }: { readonly rows: ReadonlyArray<FleetSlot> }) {
  const reporting = rows.filter((row) => row.slot.resources != null).length;
  if (reporting === rows.length) return null;
  if (reporting === 0) {
    return (
      <StateBanner tone="critical">Resource data unavailable for all displayed slots.</StateBanner>
    );
  }
  return (
    <StateBanner tone="caution">
      Partial resource data: {reporting} of {rows.length} displayed slots are reporting CPU, memory,
      and PIDs.
    </StateBanner>
  );
}

function formatRepositoryLabel(repository: string): string {
  if (!URL.canParse(repository)) return repository;
  const url = new URL(repository);
  if (url.hostname.toLocaleLowerCase() !== 'github.com') return repository;
  return url.pathname.replace(/^\/|\/$/g, '').replace(/\.git$/i, '') || repository;
}

interface RunnerIdentityProps {
  readonly row: FleetSlot;
  readonly tenantId: string;
  readonly selectionHref: string;
  readonly selected: boolean;
  readonly onSelect: () => void;
}

function RunnerIdentity({ row, tenantId, selectionHref, selected, onSelect }: RunnerIdentityProps) {
  return (
    <div className="grid min-w-28 gap-1">
      <Link
        className="w-fit max-w-full font-semibold [overflow-wrap:anywhere] underline-offset-4 hover:underline focus-visible:underline"
        to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(row.nodeId)}/profiles/${encodeURIComponent(row.profileId)}`}
      >
        {row.nodeName}
      </Link>
      <span className="flex min-w-0 flex-wrap gap-x-1 font-mono text-xs text-muted-foreground">
        <span className="min-w-0 [overflow-wrap:anywhere]">{row.profileId}</span>
        <span aria-hidden="true">·</span>
        <span className="min-w-0 [overflow-wrap:anywhere]">{row.slot.key}</span>
      </span>
      <Button asChild className="mt-1 w-fit" size="sm" variant={selected ? 'secondary' : 'outline'}>
        <Link aria-current={selected ? 'page' : undefined} to={selectionHref} onClick={onSelect}>
          {selected ? 'Selected' : 'Investigate'}
        </Link>
      </Button>
    </div>
  );
}

function RunnerWorkload({ row }: { readonly row: FleetSlot }) {
  const repository = row.slot.repository;
  const job = row.slot.currentJob;
  return (
    <div className="grid min-w-32 max-w-56 gap-1">
      <span
        className="[overflow-wrap:anywhere] font-medium"
        title={job?.displayName ?? repository ?? undefined}
      >
        {job
          ? (job.displayName ?? `GitHub job ${job.jobId}`)
          : row.slot.activity === 'busy' && row.slot.currentJob === undefined
            ? 'Job identity unavailable'
            : repository
              ? formatRepositoryLabel(repository)
              : 'Shared scope'}
      </span>
      <span className="[overflow-wrap:anywhere] text-xs text-muted-foreground">
        {job ? formatRepositoryLabel(job.repository) : (row.slot.target ?? 'Target unavailable')}
      </span>
    </div>
  );
}

function RunnerState({ row }: { readonly row: FleetSlot }) {
  return (
    <div className="flex min-w-24 flex-wrap gap-1.5">
      {!row.nodeOnline ? <StatusBadge status="offline" /> : null}
      {row.slot.activity ? <StatusBadge status={row.slot.activity} /> : null}
      <StatusBadge status={row.slot.state} />
      <span data-testid={`runner-registration-${row.nodeId}-${row.profileId}-${row.slot.key}`}>
        <StatusBadge status={row.slot.registrationStatus ?? 'unknown'} />
      </span>
    </div>
  );
}

function RunnerResources({ row }: { readonly row: FleetSlot }) {
  return (
    <div
      className="grid min-w-20 gap-1 text-xs tabular-nums"
      data-testid={`runner-resources-${row.nodeId}-${row.profileId}-${row.slot.key}`}
    >
      {row.slot.resources ? (
        <>
          <span>{formatCpuCores(row.slot.resources.cpuCores)}</span>
          <span>{formatBytes(row.slot.resources.memoryWorkingSetBytes)}</span>
          <span>{formatPids(row.slot.resources.pids)}</span>
        </>
      ) : (
        <span className="text-muted-foreground">Unavailable</span>
      )}
    </div>
  );
}

function RunnerEvidence({
  row,
  includeTestIds = true,
}: {
  readonly row: FleetSlot;
  readonly includeTestIds?: boolean;
}) {
  return (
    <div className="grid min-w-28 gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-xs tabular-nums">
          {row.slot.failureCount === 0
            ? 'No failures'
            : `${row.slot.failureCount} ${row.slot.failureCount === 1 ? 'failure' : 'failures'}`}
        </span>
        {row.slot.lastExit ? <StatusBadge status={row.slot.lastExit.classification} /> : null}
      </div>
      <details className="group">
        <summary className="min-h-9 w-fit cursor-pointer rounded-md py-2 text-xs font-medium text-muted-foreground outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring">
          Technical details
        </summary>
        <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-2 border-t pt-2 text-xs">
          <dt className="text-muted-foreground">Network</dt>
          <dd
            className="text-right tabular-nums"
            data-testid={
              includeTestIds
                ? `runner-network-${row.nodeId}-${row.profileId}-${row.slot.key}`
                : undefined
            }
          >
            {row.slot.resources
              ? `${formatOptionalBytes(row.slot.resources.networkRxBytes)} in · ${formatOptionalBytes(row.slot.resources.networkTxBytes)} out`
              : 'Unavailable'}
          </dd>
          <dt className="text-muted-foreground">Block I/O</dt>
          <dd
            className="text-right tabular-nums"
            data-testid={
              includeTestIds
                ? `runner-block-io-${row.nodeId}-${row.profileId}-${row.slot.key}`
                : undefined
            }
          >
            {row.slot.resources
              ? `${formatOptionalBytes(row.slot.resources.blockReadBytes)} read · ${formatOptionalBytes(row.slot.resources.blockWriteBytes)} written`
              : 'Unavailable'}
          </dd>
          <dt className="text-muted-foreground">Image</dt>
          <dd
            className="text-right"
            data-testid={
              includeTestIds
                ? `runner-image-${row.nodeId}-${row.profileId}-${row.slot.key}`
                : undefined
            }
          >
            <WorkerImageIdentity imageId={row.slot.imageId} />
          </dd>
          <dt className="text-muted-foreground">Last exit</dt>
          <dd
            className="min-w-0 text-right [&>span]:!whitespace-normal [&>span]:flex-wrap [&>span]:justify-end"
            data-testid={
              includeTestIds
                ? `runner-last-exit-${row.nodeId}-${row.profileId}-${row.slot.key}`
                : undefined
            }
          >
            <WorkerExitEvidence lastExit={row.slot.lastExit} />
          </dd>
          <dt className="text-muted-foreground">Updated</dt>
          <dd className="text-right">{formatTime(row.slot.updatedAt)}</dd>
        </dl>
      </details>
    </div>
  );
}

/** Displays filterable, URL-backed runner slots from the shared tenant fleet cache. */
export function RunnersPage({ tenantId }: RunnersPageProps) {
  const { fleet, error, isLoading } = useFleet();
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedHeading = useRef<HTMLHeadingElement>(null);
  const pendingSelectionFocus = useRef<string | null>(null);
  const nodeFilter = searchParams.get('node') ?? '';
  const profileFilter = searchParams.get('profile') ?? '';
  const repositoryFilter = searchParams.get('repository') ?? '';
  const activityFilter = searchParams.get('activity') ?? '';
  const registrationFilter = searchParams.get('registration') ?? '';
  const stateFilter = searchParams.get('state') ?? '';
  const exitFilter = searchParams.get('exit') ?? '';
  const requestedSort = searchParams.get('sort');
  const sort: SortKey =
    requestedSort && sortKeys.has(requestedSort as SortKey)
      ? (requestedSort as SortKey)
      : 'attention';
  const direction: SortDirection = searchParams.get('direction') === 'desc' ? 'desc' : 'asc';

  const allRows = useMemo(() => (fleet ? flattenFleetSlots(fleet) : []), [fleet]);
  const nodes = useMemo(
    () =>
      [...new Map(allRows.map((row) => [row.nodeId, row.nodeName])).entries()].sort((left, right) =>
        compareText(left[1], right[1]),
      ),
    [allRows],
  );
  const profiles = useMemo(() => uniqueSorted(allRows.map((row) => row.profileId)), [allRows]);
  const activities = useMemo(
    () => uniqueSorted(allRows.flatMap((row) => (row.slot.activity ? [row.slot.activity] : []))),
    [allRows],
  );
  const registrations = useMemo(
    () => uniqueSorted(allRows.map((row) => row.slot.registrationStatus ?? 'unknown')),
    [allRows],
  );
  const states = useMemo(() => uniqueSorted(allRows.map((row) => row.slot.state)), [allRows]);
  const exitClassifications = useMemo(
    () =>
      uniqueSorted(
        allRows.flatMap((row) => (row.slot.lastExit ? [row.slot.lastExit.classification] : [])),
      ),
    [allRows],
  );
  const rows = useMemo(() => {
    const repositoryQuery = repositoryFilter.trim().toLocaleLowerCase();
    const filtered = allRows.filter(
      (row) =>
        (!nodeFilter || row.nodeId === nodeFilter) &&
        (!profileFilter || row.profileId === profileFilter) &&
        (!repositoryQuery ||
          [
            row.slot.repository,
            row.slot.currentJob?.repository,
            row.slot.currentJob?.displayName,
            row.slot.currentJob?.jobId,
            row.slot.currentJob ? String(row.slot.currentJob.workflowRunId) : null,
          ].some((value) => value?.toLocaleLowerCase().includes(repositoryQuery))) &&
        (!activityFilter || row.slot.activity === activityFilter) &&
        (!registrationFilter ||
          (row.slot.registrationStatus ?? 'unknown') === registrationFilter) &&
        (!stateFilter || row.slot.state === stateFilter) &&
        (!exitFilter ||
          (exitFilter === 'none'
            ? row.slot.lastExit === null
            : row.slot.lastExit?.classification === exitFilter)),
    );
    return filtered.sort((left, right) => {
      const comparison = compareRows(left, right, sort);
      return direction === 'asc' ? comparison : -comparison;
    });
  }, [
    activityFilter,
    allRows,
    direction,
    exitFilter,
    nodeFilter,
    profileFilter,
    registrationFilter,
    repositoryFilter,
    sort,
    stateFilter,
  ]);
  const requestedRunnerId = searchParams.get('runner') || null;
  const requestedRow =
    requestedRunnerId == null
      ? undefined
      : allRows.find((row) => runnerSelectionId(row) === requestedRunnerId);
  const requestedRunnerIsUnavailable =
    fleet != null && requestedRunnerId != null && requestedRow == null;
  const selectedRow = requestedRunnerId == null ? (rows[0] ?? null) : (requestedRow ?? null);
  const selectedRowIsVisible =
    selectedRow != null &&
    rows.some((row) => runnerSelectionId(row) === runnerSelectionId(selectedRow));
  const selectedRowId = selectedRow ? runnerSelectionId(selectedRow) : null;
  const selectedRowIndex =
    selectedRowId == null ? -1 : rows.findIndex((row) => runnerSelectionId(row) === selectedRowId);
  const selectedRowIsPinned = selectedRowIndex >= runnerMobileLimit;
  const mobileRows =
    selectedRowIsPinned && selectedRow
      ? [
          selectedRow,
          ...rows
            .filter((row) => runnerSelectionId(row) !== selectedRowId)
            .slice(0, runnerMobileLimit - 1),
        ]
      : rows.slice(0, runnerMobileLimit);

  const selectRunner = useCallback((selectionId: string) => {
    pendingSelectionFocus.current = selectionId;
  }, []);

  useEffect(() => {
    if (selectedRow == null || pendingSelectionFocus.current !== runnerSelectionId(selectedRow)) {
      return;
    }
    pendingSelectionFocus.current = null;
    selectedHeading.current?.focus();
  }, [selectedRow]);

  useEffect(() => {
    if (requestedRunnerId != null || rows[0] == null) return;
    const initialSelectionId = runnerSelectionId(rows[0]);
    setSearchParams(
      (current) => {
        if (current.get('runner')) return current;
        const next = new URLSearchParams(current);
        next.set('runner', initialSelectionId);
        return next;
      },
      { replace: true },
    );
  }, [requestedRunnerId, rows, setSearchParams]);

  const setParameter = useCallback(
    (name: string, value: string) => {
      setSearchParams(
        (current) => {
          const next = new URLSearchParams(current);
          if (value) next.set(name, value);
          else next.delete(name);
          return next;
        },
        { replace: true },
      );
    },
    [setSearchParams],
  );

  const clearAllParameters = () => {
    setSearchParams(
      (current) => {
        const next = new URLSearchParams();
        const selected = current.get('runner');
        if (selected) next.set('runner', selected);
        return next;
      },
      { replace: true },
    );
  };

  const hasFilters = Boolean(
    nodeFilter ||
    profileFilter ||
    repositoryFilter ||
    activityFilter ||
    registrationFilter ||
    stateFilter ||
    exitFilter ||
    sort !== 'attention' ||
    direction !== 'asc',
  );
  const hasAdvancedFilters = Boolean(
    registrationFilter || stateFilter || exitFilter || sort !== 'attention' || direction !== 'asc',
  );
  const offlineCount = rows.filter((row) => !row.nodeOnline).length;
  const busyCount = rows.filter((row) => row.slot.activity === 'busy').length;
  const currentJobCount = allRows.filter((row) => row.slot.currentJob != null).length;
  const reviewCount = allRows.filter(runnerNeedsReview).length;
  const resultSummary = `Showing ${rows.length} of ${allRows.length} ${allRows.length === 1 ? 'slot' : 'slots'} · ${busyCount} busy · ${offlineCount} offline`;
  const chips = useMemo<ReadonlyArray<FilterChipDescriptor>>(() => {
    const activeNode = nodes.find(([nodeId]) => nodeId === nodeFilter);
    return [
      nodeFilter
        ? {
            key: 'node',
            label: 'Node',
            value: activeNode?.[1] ?? nodeFilter,
            onRemove: () => setParameter('node', ''),
          }
        : null,
      profileFilter
        ? {
            key: 'profile',
            label: 'Profile',
            value: profileFilter,
            onRemove: () => setParameter('profile', ''),
          }
        : null,
      repositoryFilter
        ? {
            key: 'repository',
            label: 'Repository',
            value: repositoryFilter,
            onRemove: () => setParameter('repository', ''),
          }
        : null,
      activityFilter
        ? {
            key: 'activity',
            label: 'Activity',
            value: activityFilter,
            onRemove: () => setParameter('activity', ''),
          }
        : null,
      registrationFilter
        ? {
            key: 'registration',
            label: 'Registration',
            value: formatFilterValue(registrationFilter),
            onRemove: () => setParameter('registration', ''),
          }
        : null,
      stateFilter
        ? {
            key: 'state',
            label: 'State',
            value: stateFilter,
            onRemove: () => setParameter('state', ''),
          }
        : null,
      exitFilter
        ? {
            key: 'exit',
            label: 'Exit',
            value:
              exitFilter === 'none' ? 'No exit evidence recorded' : formatFilterValue(exitFilter),
            onRemove: () => setParameter('exit', ''),
          }
        : null,
      sort !== 'attention'
        ? {
            key: 'sort',
            label: 'Sort',
            value: sortLabels[sort],
            onRemove: () => setParameter('sort', ''),
          }
        : null,
      direction !== 'asc'
        ? {
            key: 'direction',
            label: 'Direction',
            value: direction === 'desc' ? 'Descending' : 'Ascending',
            onRemove: () => setParameter('direction', ''),
          }
        : null,
    ].filter((chip): chip is FilterChipDescriptor & { onRemove: () => void } => chip !== null);
  }, [
    activityFilter,
    direction,
    exitFilter,
    nodeFilter,
    nodes,
    profileFilter,
    registrationFilter,
    repositoryFilter,
    setParameter,
    sort,
    stateFilter,
  ]);

  return (
    <section className="grid min-w-0 max-w-full gap-4">
      <ReadinessSummary
        title="Runner readiness"
        description="Explicit current-job correlation, runner lifecycle, GitHub registration, and unavailable evidence across this tenant."
        status={
          <StatusBadge
            status={
              !fleet
                ? error
                  ? 'Status unavailable'
                  : 'Loading'
                : reviewCount > 0
                  ? 'Needs review'
                  : currentJobCount > 0
                    ? 'Current work'
                    : 'No reported exception'
            }
            tone={
              !fleet ? (error ? 'critical' : 'neutral') : reviewCount > 0 ? 'caution' : 'positive'
            }
          />
        }
        items={[
          {
            label: 'Observation',
            value: fleet ? formatTime(fleet.generatedAt) : error ? 'Unavailable' : 'Loading…',
            detail: 'Latest accepted fleet projection',
          },
          {
            label: 'Current jobs',
            value: fleet ? currentJobCount : error ? 'Unavailable' : 'Loading…',
            detail: 'Explicit manager-reported job correlation',
          },
          {
            label: 'Slots needing review',
            value: fleet ? reviewCount : error ? 'Unavailable' : 'Loading…',
            detail: 'Busy without job, offline, degraded, or incomplete evidence',
          },
          {
            label: 'Reported inventory',
            value: fleet ? allRows.length : error ? 'Unavailable' : 'Loading…',
            detail: 'Slots across every node and profile',
          },
        ]}
      />

      {isLoading && !fleet ? <LoadingState label="Loading runners…" /> : null}
      {error && fleet ? (
        <StateBanner tone="caution">
          Showing stale runner data because the latest fleet refresh failed: {error}
        </StateBanner>
      ) : null}
      {error && !fleet ? (
        <StateBanner tone="critical">Runner data is unavailable: {error}</StateBanner>
      ) : null}

      {fleet ? (
        <>
          <FilterToolbar>
            <FormField label="Node">
              <select
                className={inputClassName}
                value={nodeFilter}
                onChange={(event) => setParameter('node', event.target.value)}
              >
                <option value="">All nodes</option>
                {nodes.map(([nodeId, nodeName]) => (
                  <option key={nodeId} value={nodeId}>
                    {nodeName}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Profile">
              <select
                className={inputClassName}
                value={profileFilter}
                onChange={(event) => setParameter('profile', event.target.value)}
              >
                <option value="">All profiles</option>
                {profiles.map((profile) => (
                  <option key={profile} value={profile}>
                    {profile}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Repository">
              <input
                className={inputClassName}
                type="search"
                value={repositoryFilter}
                onChange={(event) => setParameter('repository', event.target.value)}
              />
            </FormField>
            <FormField label="Activity">
              <select
                className={inputClassName}
                value={activityFilter}
                onChange={(event) => setParameter('activity', event.target.value)}
              >
                <option value="">All activity</option>
                {activities.map((activity) => (
                  <option key={activity} value={activity}>
                    {activity}
                  </option>
                ))}
              </select>
            </FormField>
          </FilterToolbar>

          <details className="rounded-lg border bg-card" open={hasAdvancedFilters}>
            <summary className="cursor-pointer list-none px-4 py-3 text-sm font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
              Advanced filters and sorting
            </summary>
            <div className="border-t px-4 py-4">
              <FilterToolbar className="border-0 bg-transparent p-0 shadow-none">
                <FormField label="GitHub registration">
                  <select
                    className={inputClassName}
                    value={registrationFilter}
                    onChange={(event) => setParameter('registration', event.target.value)}
                  >
                    <option value="">All registration states</option>
                    {registrations.map((registration) => (
                      <option key={registration} value={registration}>
                        {formatFilterValue(registration)}
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Lifecycle state">
                  <select
                    className={inputClassName}
                    value={stateFilter}
                    onChange={(event) => setParameter('state', event.target.value)}
                  >
                    <option value="">All states</option>
                    {states.map((state) => (
                      <option key={state} value={state}>
                        {state}
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Last exit">
                  <select
                    className={inputClassName}
                    value={exitFilter}
                    onChange={(event) => setParameter('exit', event.target.value)}
                  >
                    <option value="">All exit evidence</option>
                    <option value="none">No exit evidence recorded</option>
                    {exitClassifications.map((classification) => (
                      <option key={classification} value={classification}>
                        {formatFilterValue(classification)}
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Sort by">
                  <select
                    className={inputClassName}
                    value={sort}
                    onChange={(event) => setParameter('sort', event.target.value)}
                  >
                    {Object.entries(sortLabels).map(([value, label]) => (
                      <option key={value} value={value}>
                        {label}
                      </option>
                    ))}
                  </select>
                </FormField>
                <FormField label="Sort direction">
                  <select
                    className={inputClassName}
                    value={direction}
                    onChange={(event) => setParameter('direction', event.target.value)}
                  >
                    <option value="asc">Ascending</option>
                    <option value="desc">Descending</option>
                  </select>
                </FormField>
              </FilterToolbar>
            </div>
          </details>

          <FilterChips
            chips={chips}
            resultSummary={resultSummary}
            onClearAll={hasFilters ? clearAllParameters : undefined}
          />

          {requestedRunnerIsUnavailable ? (
            <StateBanner tone="caution" role="status">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <p className="font-semibold">Selected runner is unavailable</p>
                  <p className="mt-1">
                    The requested node, profile, and slot tuple is not present in the latest fleet
                    projection. Another runner has not been substituted.
                  </p>
                </div>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => setParameter('runner', '')}
                >
                  Clear selection
                </Button>
              </div>
            </StateBanner>
          ) : null}
          {selectedRow ? (
            <div aria-label="Selected runner investigation" className="min-w-0" role="region">
              <RunnerDetail
                row={selectedRow}
                tenantId={tenantId}
                isVisible={selectedRowIsVisible}
                focusTitleRef={selectedHeading}
              />
            </div>
          ) : null}

          {allRows.length === 0 ? (
            <EmptyState
              title="No runner slots reported"
              description="No node or profile has reported a runner slot for this tenant. This does not prove that no external GitHub runner exists."
            />
          ) : null}
          {allRows.length > 0 && rows.length === 0 ? (
            <EmptyState
              title="No runners match this view"
              description="Change or clear the filters to return to the reported runner inventory."
            />
          ) : null}
          {rows.length > 0 ? (
            <>
              {offlineCount > 0 ? (
                <StateBanner tone="caution">
                  {offlineCount} displayed {offlineCount === 1 ? 'slot is' : 'slots are'} from
                  offline nodes and may be stale.
                </StateBanner>
              ) : null}
              <ResourceNotice rows={rows} />
              <div>
                <h2 className="text-base font-semibold">Runner inventory</h2>
                <p className="text-sm text-muted-foreground">
                  Attention-ordered runner slots with the wide comparison table preserved.
                </p>
              </div>

              <div className="grid gap-3 lg:hidden" data-testid="runners-mobile-summary">
                {mobileRows.map((row) => {
                  const selectionId = runnerSelectionId(row);
                  return (
                    <div
                      key={selectionId}
                      className="grid gap-3 rounded-lg border bg-card p-4"
                      data-testid={`runner-card-${row.nodeId}-${row.profileId}-${row.slot.key}`}
                    >
                      <RunnerIdentity
                        row={row}
                        tenantId={tenantId}
                        selectionHref={runnerSelectionHref(searchParams, row)}
                        selected={
                          selectedRow != null && selectionId === runnerSelectionId(selectedRow)
                        }
                        onSelect={() => selectRunner(selectionId)}
                      />
                      <RunnerWorkload row={row} />
                      <div className="flex flex-wrap gap-1.5">
                        {!row.nodeOnline ? <StatusBadge status="offline" /> : null}
                        {row.slot.activity ? <StatusBadge status={row.slot.activity} /> : null}
                        <StatusBadge status={row.slot.state} />
                        <StatusBadge status={row.slot.registrationStatus ?? 'unknown'} />
                      </div>
                      <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                        {row.slot.resources ? (
                          <span className="tabular-nums">
                            {formatCpuCores(row.slot.resources.cpuCores)} ·{' '}
                            {formatBytes(row.slot.resources.memoryWorkingSetBytes)} ·{' '}
                            {formatPids(row.slot.resources.pids)}
                          </span>
                        ) : (
                          <span>Resources unavailable</span>
                        )}
                      </div>
                      <RunnerEvidence row={row} includeTestIds={false} />
                    </div>
                  );
                })}
                {rows.length > runnerMobileLimit ? (
                  <p className="text-xs text-muted-foreground">
                    Showing {runnerMobileLimit} of {rows.length} slots.
                    {selectedRowIsPinned
                      ? ' The selected runner is pinned in this window.'
                      : ''}{' '}
                    Use filters to narrow results.
                  </p>
                ) : null}
              </div>

              <OperationalTable
                caption="Runner slots for the active tenant"
                className="hidden lg:block"
                columns={tableColumns}
                minWidthClassName="min-w-[42rem]"
              >
                {rows.map((row) => {
                  const selectionId = runnerSelectionId(row);
                  return (
                    <tr
                      key={selectionId}
                      className="border-t"
                      data-testid={`runner-row-${row.nodeId}-${row.profileId}-${row.slot.key}`}
                    >
                      <td
                        className="px-3 py-3 align-top"
                        data-testid={`runner-slot-${row.nodeId}-${row.profileId}-${row.slot.key}`}
                      >
                        <RunnerIdentity
                          row={row}
                          tenantId={tenantId}
                          selectionHref={runnerSelectionHref(searchParams, row)}
                          selected={
                            selectedRow != null && selectionId === runnerSelectionId(selectedRow)
                          }
                          onSelect={() => selectRunner(selectionId)}
                        />
                      </td>
                      <td className="px-3 py-3 align-top">
                        <RunnerWorkload row={row} />
                      </td>
                      <td className="px-3 py-3 align-top">
                        <RunnerState row={row} />
                      </td>
                      <td className="px-3 py-3 align-top">
                        <RunnerResources row={row} />
                      </td>
                      <td className="px-3 py-3 align-top">
                        <RunnerEvidence row={row} />
                      </td>
                    </tr>
                  );
                })}
              </OperationalTable>
            </>
          ) : null}
          {hasFilters ? (
            <p className="text-xs text-muted-foreground">
              Filter and sort state is saved in this page URL.
            </p>
          ) : null}
        </>
      ) : null}
    </section>
  );
}

function runnerSelectionHref(searchParams: URLSearchParams, row: FleetSlot): string {
  const next = new URLSearchParams(searchParams);
  next.set('runner', runnerSelectionId(row));
  return `?${next.toString()}`;
}
