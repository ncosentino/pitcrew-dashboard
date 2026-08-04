import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useFleet, type FleetNode } from '@/core/fleet';
import { formatBytes, formatCpuCores, formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { cn } from '@/lib/utils';

import {
  aggregateNode,
  getNodeStatus,
  selectNodes,
  type NodeSort,
  type NodeStatusFilter,
} from './nodeSummary';
import { HardwareComparison } from './components/HostHardwareSummary';

type FleetDensity = 'comfortable' | 'compact';

/** Browser storage key for the fleet overview density preference. */
export const fleetDensityStorageKey = 'pitcrew-dashboard-fleet-density';

function readDensity(): FleetDensity {
  try {
    return globalThis.localStorage.getItem(fleetDensityStorageKey) === 'compact'
      ? 'compact'
      : 'comfortable';
  } catch (error) {
    if (error instanceof DOMException) {
      console.warn('The dashboard could not read the saved fleet density.', error);
      return 'comfortable';
    }
    throw error;
  }
}

function storeDensity(density: FleetDensity): void {
  try {
    globalThis.localStorage.setItem(fleetDensityStorageKey, density);
  } catch (error) {
    if (error instanceof DOMException) {
      console.warn('The dashboard could not save the fleet density.', error);
      return;
    }
    throw error;
  }
}

interface NodeSummaryRowProps {
  readonly node: FleetNode;
  readonly tenantId: string;
  readonly density: FleetDensity;
  readonly selected: boolean;
  readonly onSelectionChanged: (nodeId: string, selected: boolean) => void;
}

function NodeSummaryRow({
  node,
  tenantId,
  density,
  selected,
  onSelectionChanged,
}: NodeSummaryRowProps) {
  const aggregate = aggregateNode(node);
  const status = getNodeStatus(node);
  const resources = aggregate.resources;
  return (
    <tr className="border-t" data-testid={`fleet-node-${node.nodeId}`}>
      <td className={cn('px-4', density === 'compact' ? 'py-2' : 'py-4')}>
        <label className="mb-2 flex items-center gap-2 text-xs text-muted-foreground">
          <input
            aria-label={`Compare ${node.displayName}`}
            checked={selected}
            onChange={(event) => onSelectionChanged(node.nodeId, event.currentTarget.checked)}
            type="checkbox"
          />
          Compare hardware
        </label>
        <Link
          className="font-semibold text-primary underline-offset-4 hover:underline"
          to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(node.nodeId)}`}
        >
          {node.displayName}
        </Link>
        <div className="mt-1 font-mono text-xs text-muted-foreground">{node.nodeId}</div>
      </td>
      <td className="px-4 py-2">
        <StatusBadge status={status} />
      </td>
      <td className="px-4 py-2">{node.connectorVersion || 'Unknown'}</td>
      <td className="px-4 py-2">{formatTime(node.lastSeenAt)}</td>
      <td className="px-4 py-2 text-right tabular-nums">{node.profiles.length}</td>
      <td className="px-4 py-2 text-right tabular-nums">
        {aggregate.configuredSlots} / {aggregate.activeSlots} /{' '}
        {aggregate.eligibleSlots ?? 'Unknown'}
      </td>
      <td className="px-4 py-2 text-right tabular-nums">
        {resources.reportingSources > 0 ? (
          <div className="grid justify-items-end gap-1">
            <span>
              {formatCpuCores(resources.cpuCores)} / {formatBytes(resources.memoryWorkingSetBytes)}
            </span>
            <span className="flex items-center gap-2 text-xs text-muted-foreground">
              {resources.reportingSources} of {resources.totalSources} sources
              <StatusBadge status={resources.status} />
            </span>
          </div>
        ) : (
          <span className="text-muted-foreground">Unavailable</span>
        )}
      </td>
    </tr>
  );
}

/** Renders scan-friendly node summaries from the shared tenant fleet projection. */
export default function FleetOverviewPage() {
  const { tenantId = '' } = useParams();
  const { fleet, error, isLoading } = useFleet();
  const [status, setStatus] = useState<NodeStatusFilter>('all');
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState<NodeSort>('name');
  const [density, setDensity] = useState<FleetDensity>(readDensity);
  const [selectedNodeIds, setSelectedNodeIds] = useState<readonly string[]>([]);
  const nodes = selectNodes(fleet?.nodes ?? [], status, query, sort);
  const selectedNodes = (fleet?.nodes ?? []).filter((node) =>
    selectedNodeIds.includes(node.nodeId),
  );

  const changeDensity = (nextDensity: FleetDensity) => {
    setDensity(nextDensity);
    storeDensity(nextDensity);
  };

  const changeSelection = (nodeId: string, selected: boolean) => {
    setSelectedNodeIds((current) => {
      if (!selected) return current.filter((candidate) => candidate !== nodeId);
      if (current.includes(nodeId) || current.length >= 4) return current;
      return [...current, nodeId];
    });
  };

  return (
    <>
      <section className="grid gap-2">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-2xl font-bold tracking-tight">Fleet status</h2>
            <p className="text-sm text-muted-foreground">
              Node-level capacity and health across this tenant.
            </p>
          </div>
          <div className="text-right text-sm text-muted-foreground">
            {fleet ? `Updated ${formatTime(fleet.generatedAt)}` : 'Waiting for status'}
          </div>
        </div>
      </section>

      {error ? (
        <div
          className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
          role={fleet ? 'status' : 'alert'}
        >
          {fleet ? `Showing stale fleet data. ${error}` : error}
        </div>
      ) : null}

      {isLoading && !fleet ? <p className="text-muted-foreground">Loading fleet status…</p> : null}

      {!isLoading && fleet?.nodes.length === 0 ? (
        <Card>
          <CardHeader>
            <CardTitle>No servers enrolled</CardTitle>
            <CardDescription>
              Create a one-time code, configure it on a connector, and start the connector.
            </CardDescription>
          </CardHeader>
        </Card>
      ) : null}

      {fleet && fleet.nodes.length > 0 ? (
        <>
          <section className="grid gap-3 rounded-lg border bg-card p-4 sm:grid-cols-2 xl:grid-cols-4">
            <label className="grid gap-1 text-sm font-medium">
              Search nodes
              <input
                className="h-9 rounded-md border bg-background px-3 text-sm"
                type="search"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
              />
            </label>
            <label className="grid gap-1 text-sm font-medium">
              Status
              <select
                className="h-9 rounded-md border bg-background px-3 text-sm"
                value={status}
                onChange={(event) => setStatus(event.target.value as NodeStatusFilter)}
              >
                <option value="all">All states</option>
                <option value="online">Online</option>
                <option value="offline">Offline</option>
                <option value="revoked">Revoked</option>
              </select>
            </label>
            <label className="grid gap-1 text-sm font-medium">
              Sort by
              <select
                className="h-9 rounded-md border bg-background px-3 text-sm"
                value={sort}
                onChange={(event) => setSort(event.target.value as NodeSort)}
              >
                <option value="name">Display name</option>
                <option value="status">Status</option>
                <option value="lastSeen">Last seen</option>
              </select>
            </label>
            <label className="grid gap-1 text-sm font-medium">
              Density
              <select
                className="h-9 rounded-md border bg-background px-3 text-sm"
                value={density}
                onChange={(event) => changeDensity(event.target.value as FleetDensity)}
              >
                <option value="comfortable">Comfortable</option>
                <option value="compact">Compact</option>
              </select>
            </label>
          </section>

          <HardwareComparison nodes={selectedNodes} />

          {nodes.length === 0 ? (
            <Card>
              <CardHeader>
                <CardTitle>No matching nodes</CardTitle>
                <CardDescription>
                  Adjust the status or text filter to see more nodes.
                </CardDescription>
              </CardHeader>
            </Card>
          ) : (
            <section className="overflow-x-auto rounded-lg border bg-card">
              <table className="w-full min-w-5xl text-left text-sm">
                <caption className="p-3 text-left text-sm font-semibold">
                  Fleet nodes for the active tenant
                </caption>
                <thead className="bg-muted/50 text-xs text-muted-foreground uppercase">
                  <tr>
                    <th scope="col" className="px-4 py-3 font-medium">
                      Node
                    </th>
                    <th scope="col" className="px-4 py-3 font-medium">
                      State
                    </th>
                    <th scope="col" className="px-4 py-3 font-medium">
                      Connector
                    </th>
                    <th scope="col" className="px-4 py-3 font-medium">
                      Last seen
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-medium">
                      Profiles
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-medium">
                      Configured / local / GitHub eligible
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-medium">
                      Current CPU / memory
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {nodes.map((node) => (
                    <NodeSummaryRow
                      key={node.nodeId}
                      node={node}
                      tenantId={tenantId}
                      density={density}
                      selected={selectedNodeIds.includes(node.nodeId)}
                      onSelectionChanged={changeSelection}
                    />
                  ))}
                </tbody>
              </table>
            </section>
          )}
        </>
      ) : null}
    </>
  );
}
