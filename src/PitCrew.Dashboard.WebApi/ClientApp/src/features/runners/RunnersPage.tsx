import { useCallback, useMemo } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useFleet } from '@/core/fleet';
import {
  formatBytes,
  formatCpuCores,
  formatOptionalBytes,
  formatPids,
} from '@/core/formatting/formatters';
import { FilterChips, type FilterChipDescriptor } from '@/core/ui/FilterChips';
import { FilterToolbar } from '@/core/ui/FilterToolbar';
import { FormField } from '@/core/ui/FormField';
import { OperationalTable, type OperationalTableColumn } from '@/core/ui/OperationalTable';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { WorkerExitEvidence, WorkerImageIdentity } from '@/core/ui/WorkerEvidenceCells';

import { flattenFleetSlots, type FleetSlot } from './runnerRows';

interface RunnersPageProps {
  readonly tenantId: string;
}

type SortKey =
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

const inputClassName = 'h-9 rounded-md border bg-background px-3 text-sm';
const tableColumns: ReadonlyArray<OperationalTableColumn> = [
  { key: 'node', header: 'Node' },
  { key: 'profile', header: 'Profile' },
  { key: 'slot', header: 'Slot' },
  { key: 'repository', header: 'Repository' },
  { key: 'target', header: 'Target' },
  { key: 'activity', header: 'Activity' },
  { key: 'registration', header: 'GitHub registration' },
  { key: 'state', header: 'Local lifecycle state' },
  { key: 'failures', header: 'Failures', align: 'right' },
  { key: 'cpu', header: 'CPU', align: 'right' },
  { key: 'memory', header: 'Memory', align: 'right' },
  { key: 'pids', header: 'PIDs', align: 'right' },
  { key: 'network', header: 'Network I/O', align: 'right' },
  { key: 'block', header: 'Block I/O', align: 'right' },
  { key: 'image', header: 'Worker image' },
  { key: 'exit', header: 'Last exit' },
];
const sortLabels: Record<SortKey, string> = {
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

/** Displays filterable, URL-backed runner slots from the shared tenant fleet cache. */
export function RunnersPage({ tenantId }: RunnersPageProps) {
  const { fleet, error, isLoading } = useFleet();
  const [searchParams, setSearchParams] = useSearchParams();
  const nodeFilter = searchParams.get('node') ?? '';
  const profileFilter = searchParams.get('profile') ?? '';
  const repositoryFilter = searchParams.get('repository') ?? '';
  const activityFilter = searchParams.get('activity') ?? '';
  const registrationFilter = searchParams.get('registration') ?? '';
  const stateFilter = searchParams.get('state') ?? '';
  const exitFilter = searchParams.get('exit') ?? '';
  const requestedSort = searchParams.get('sort');
  const sort: SortKey =
    requestedSort && sortKeys.has(requestedSort as SortKey) ? (requestedSort as SortKey) : 'node';
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
          (row.slot.repository ?? '').toLocaleLowerCase().includes(repositoryQuery)) &&
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
    setSearchParams(new URLSearchParams(), { replace: true });
  };

  const hasFilters = Boolean(
    nodeFilter ||
    profileFilter ||
    repositoryFilter ||
    activityFilter ||
    registrationFilter ||
    stateFilter ||
    exitFilter ||
    sort !== 'node' ||
    direction !== 'asc',
  );
  const hasAdvancedFilters = Boolean(
    registrationFilter || stateFilter || exitFilter || sort !== 'node' || direction !== 'asc',
  );
  const offlineCount = rows.filter((row) => !row.nodeOnline).length;
  const busyCount = rows.filter((row) => row.slot.activity === 'busy').length;
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
      sort !== 'node'
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
      <p className="text-sm text-muted-foreground">
        Local lifecycle and GitHub registration across every node and profile in this tenant.
      </p>

      {isLoading && !fleet ? <p className="text-muted-foreground">Loading runners…</p> : null}
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

          {allRows.length === 0 ? (
            <p className="rounded-lg border p-4 text-sm text-muted-foreground">
              No runner slots have been reported for this tenant.
            </p>
          ) : null}
          {allRows.length > 0 && rows.length === 0 ? (
            <p className="rounded-lg border p-4 text-sm text-muted-foreground">
              No runner slots match the current filters.
            </p>
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

              <div className="grid gap-3 lg:hidden" data-testid="runners-mobile-summary">
                {rows.slice(0, 50).map((row) => (
                  <div
                    key={`${row.nodeId}-${row.profileId}-${row.slot.key}`}
                    className="grid gap-1.5 rounded-lg border bg-card p-4"
                    data-testid={`runner-card-${row.nodeId}-${row.profileId}-${row.slot.key}`}
                  >
                    <div className="flex min-w-0 flex-wrap items-center gap-2">
                      <span className="min-w-0 break-words font-medium">{row.nodeName}</span>
                      {!row.nodeOnline ? <StatusBadge status="offline" /> : null}
                      <StatusBadge status={row.slot.state} />
                      <StatusBadge status={row.slot.registrationStatus ?? 'unknown'} />
                    </div>
                    <div className="min-w-0 break-all font-mono text-xs text-muted-foreground">
                      {row.profileId} / {row.slot.key}
                    </div>
                    {row.slot.activity ? (
                      <div className="flex items-center gap-2">
                        <span className="text-xs text-muted-foreground">Activity:</span>
                        <StatusBadge status={row.slot.activity} />
                      </div>
                    ) : null}
                    {row.slot.repository ? (
                      <div className="min-w-0 break-words text-xs">{row.slot.repository}</div>
                    ) : null}
                    <div className="text-xs text-muted-foreground">
                      Failures: <span className="tabular-nums">{row.slot.failureCount}</span>
                    </div>
                    <Link
                      className="min-h-11 inline-flex items-center justify-self-start rounded-md border px-3 text-sm font-medium hover:bg-muted focus-visible:ring-2 focus-visible:ring-ring"
                      to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(row.nodeId)}/profiles/${encodeURIComponent(row.profileId)}`}
                    >
                      View profile
                    </Link>
                  </div>
                ))}
                {rows.length > 50 ? (
                  <p className="text-xs text-muted-foreground">
                    Showing 50 of {rows.length} slots. Use filters to narrow results.
                  </p>
                ) : null}
              </div>

              <OperationalTable
                caption="Runner slots for the active tenant"
                className="hidden lg:block"
                columns={tableColumns}
                minWidthClassName="min-w-6xl"
              >
                {rows.map((row) => (
                  <tr
                    key={`${row.nodeId}-${row.profileId}-${row.slot.key}`}
                    className="border-t"
                    data-testid={`runner-row-${row.nodeId}-${row.profileId}-${row.slot.key}`}
                  >
                    <td className="px-3 py-2">
                      <div>{row.nodeName}</div>
                      {!row.nodeOnline ? <StatusBadge status="offline" /> : null}
                    </td>
                    <td className="px-3 py-2">
                      <Link
                        className="font-medium underline-offset-4 hover:underline focus-visible:underline"
                        to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(row.nodeId)}/profiles/${encodeURIComponent(row.profileId)}`}
                      >
                        {row.profileId}
                      </Link>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{row.slot.key}</td>
                    <td className="px-3 py-2">{row.slot.repository ?? 'Shared scope'}</td>
                    <td className="px-3 py-2">{row.slot.target ?? 'Unavailable'}</td>
                    <td className="px-3 py-2">
                      {row.slot.activity ? (
                        <StatusBadge status={row.slot.activity} />
                      ) : (
                        'Unavailable'
                      )}
                    </td>
                    <td
                      className="px-3 py-2"
                      data-testid={`runner-registration-${row.nodeId}-${row.profileId}-${row.slot.key}`}
                    >
                      <StatusBadge status={row.slot.registrationStatus ?? 'unknown'} />
                    </td>
                    <td className="px-3 py-2">
                      <StatusBadge status={row.slot.state} />
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">{row.slot.failureCount}</td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {row.slot.resources
                        ? formatCpuCores(row.slot.resources.cpuCores)
                        : 'Unavailable'}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {row.slot.resources
                        ? formatBytes(row.slot.resources.memoryWorkingSetBytes)
                        : 'Unavailable'}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {row.slot.resources ? formatPids(row.slot.resources.pids) : 'Unavailable'}
                    </td>
                    <td
                      className="px-3 py-2 text-right tabular-nums"
                      data-testid={`runner-network-${row.nodeId}-${row.profileId}-${row.slot.key}`}
                    >
                      {row.slot.resources
                        ? `${formatOptionalBytes(row.slot.resources.networkRxBytes)} in · ${formatOptionalBytes(row.slot.resources.networkTxBytes)} out`
                        : 'Unavailable'}
                    </td>
                    <td
                      className="px-3 py-2 text-right tabular-nums"
                      data-testid={`runner-block-io-${row.nodeId}-${row.profileId}-${row.slot.key}`}
                    >
                      {row.slot.resources
                        ? `${formatOptionalBytes(row.slot.resources.blockReadBytes)} read · ${formatOptionalBytes(row.slot.resources.blockWriteBytes)} written`
                        : 'Unavailable'}
                    </td>
                    <td
                      className="px-3 py-2"
                      data-testid={`runner-image-${row.nodeId}-${row.profileId}-${row.slot.key}`}
                    >
                      <WorkerImageIdentity imageId={row.slot.imageId} />
                    </td>
                    <td
                      className="px-3 py-2"
                      data-testid={`runner-last-exit-${row.nodeId}-${row.profileId}-${row.slot.key}`}
                    >
                      <WorkerExitEvidence lastExit={row.slot.lastExit} />
                    </td>
                  </tr>
                ))}
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
