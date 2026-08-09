import { useMemo } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useFleet } from '@/core/fleet';
import {
  formatBytes,
  formatCpuCores,
  formatOptionalBytes,
  formatPids,
} from '@/core/formatting/formatters';
import { ScrollableRegion } from '@/core/ui/ScrollableRegion';
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

function ResourceNotice({ rows }: { readonly rows: ReadonlyArray<FleetSlot> }) {
  const reporting = rows.filter((row) => row.slot.resources != null).length;
  if (reporting === rows.length) return null;
  if (reporting === 0) {
    return (
      <p className="rounded-lg border border-red-300 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100">
        Resource data unavailable for all displayed slots.
      </p>
    );
  }
  return (
    <p className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
      Partial resource data: {reporting} of {rows.length} displayed slots are reporting CPU, memory,
      and PIDs.
    </p>
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

  const setParameter = (name: string, value: string) => {
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current);
        if (value) next.set(name, value);
        else next.delete(name);
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
    exitFilter,
  );
  const offlineCount = rows.filter((row) => !row.nodeOnline).length;

  return (
    <section className="grid min-w-0 max-w-full gap-4">
      <div>
        <h2 className="text-2xl font-bold tracking-tight">Runners and slots</h2>
        <p className="text-sm text-muted-foreground">
          Local lifecycle and GitHub registration across every node and profile in this tenant.
        </p>
      </div>

      {isLoading && !fleet ? <p className="text-muted-foreground">Loading runners…</p> : null}
      {error && fleet ? (
        <p className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
          Showing stale runner data because the latest fleet refresh failed: {error}
        </p>
      ) : null}
      {error && !fleet ? (
        <p className="rounded-lg border border-red-300 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100">
          Runner data is unavailable: {error}
        </p>
      ) : null}

      {fleet ? (
        <>
          <fieldset className="grid min-w-0 gap-3 rounded-lg border p-4 sm:grid-cols-2 lg:grid-cols-4">
            <legend className="px-1 text-sm font-semibold">Runner filters and sorting</legend>
            <label className="grid gap-1 text-sm" htmlFor="runners-node-filter">
              Node
              <select
                id="runners-node-filter"
                className="h-9 rounded-md border bg-background px-3"
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
            </label>
            <label className="grid gap-1 text-sm" htmlFor="runners-profile-filter">
              Profile
              <select
                id="runners-profile-filter"
                className="h-9 rounded-md border bg-background px-3"
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
            </label>
            <label className="grid gap-1 text-sm" htmlFor="runners-repository-filter">
              Repository
              <input
                id="runners-repository-filter"
                className="h-9 rounded-md border bg-background px-3"
                type="search"
                value={repositoryFilter}
                onChange={(event) => setParameter('repository', event.target.value)}
              />
            </label>
            <label className="grid gap-1 text-sm" htmlFor="runners-activity-filter">
              Activity
              <select
                id="runners-activity-filter"
                className="h-9 rounded-md border bg-background px-3"
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
            </label>
            <label className="grid gap-1 text-sm" htmlFor="runners-registration-filter">
              GitHub registration
              <select
                id="runners-registration-filter"
                className="h-9 rounded-md border bg-background px-3"
                value={registrationFilter}
                onChange={(event) => setParameter('registration', event.target.value)}
              >
                <option value="">All registration states</option>
                {registrations.map((registration) => (
                  <option key={registration} value={registration}>
                    {registration.replaceAll('-', ' ')}
                  </option>
                ))}
              </select>
            </label>
            <label className="grid gap-1 text-sm" htmlFor="runners-state-filter">
              Lifecycle state
              <select
                id="runners-state-filter"
                className="h-9 rounded-md border bg-background px-3"
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
            </label>
            <label className="grid gap-1 text-sm" htmlFor="runners-exit-filter">
              Last exit
              <select
                id="runners-exit-filter"
                className="h-9 rounded-md border bg-background px-3"
                value={exitFilter}
                onChange={(event) => setParameter('exit', event.target.value)}
              >
                <option value="">All exit evidence</option>
                <option value="none">No exit evidence recorded</option>
                {exitClassifications.map((classification) => (
                  <option key={classification} value={classification}>
                    {classification.replaceAll('-', ' ')}
                  </option>
                ))}
              </select>
            </label>
            <label className="grid gap-1 text-sm" htmlFor="runners-sort">
              Sort by
              <select
                id="runners-sort"
                className="h-9 rounded-md border bg-background px-3"
                value={sort}
                onChange={(event) => setParameter('sort', event.target.value)}
              >
                <option value="node">Node</option>
                <option value="profile">Profile</option>
                <option value="slot">Slot</option>
                <option value="repository">Repository</option>
                <option value="target">Target</option>
                <option value="activity">Activity</option>
                <option value="registration">GitHub registration</option>
                <option value="state">Lifecycle state</option>
                <option value="failures">Failure count</option>
                <option value="cpu">CPU</option>
                <option value="memory">Memory</option>
                <option value="pids">PIDs</option>
                <option value="network">Network I/O</option>
                <option value="block">Block I/O</option>
                <option value="image">Worker image</option>
                <option value="exit">Last exit</option>
              </select>
            </label>
            <label className="grid gap-1 text-sm" htmlFor="runners-sort-direction">
              Sort direction
              <select
                id="runners-sort-direction"
                className="h-9 rounded-md border bg-background px-3"
                value={direction}
                onChange={(event) => setParameter('direction', event.target.value)}
              >
                <option value="asc">Ascending</option>
                <option value="desc">Descending</option>
              </select>
            </label>
          </fieldset>

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
                <p className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
                  {offlineCount} displayed {offlineCount === 1 ? 'slot is' : 'slots are'} from
                  offline nodes and may be stale.
                </p>
              ) : null}
              <ResourceNotice rows={rows} />

              {/* Mobile summary cards */}
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

              {/* Desktop full evidence table */}
              <ScrollableRegion
                className="hidden rounded-lg border lg:block"
                label="Runner slots for the active tenant"
              >
                <table className="w-full min-w-6xl text-left text-sm">
                  <caption className="p-3 text-left text-sm font-semibold">
                    Runner slots for the active tenant
                  </caption>
                  <thead className="bg-muted/30 text-xs text-muted-foreground uppercase">
                    <tr>
                      <th scope="col" className="px-3 py-2 font-medium">
                        Node
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        Profile
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        Slot
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        Repository
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        Target
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        Activity
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        GitHub registration
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        Local lifecycle state
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-medium">
                        Failures
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-medium">
                        CPU
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-medium">
                        Memory
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-medium">
                        PIDs
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-medium">
                        Network I/O
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-medium">
                        Block I/O
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        Worker image
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        Last exit
                      </th>
                    </tr>
                  </thead>
                  <tbody>
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
                        <td className="px-3 py-2 text-right tabular-nums">
                          {row.slot.failureCount}
                        </td>
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
                  </tbody>
                </table>
              </ScrollableRegion>
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
