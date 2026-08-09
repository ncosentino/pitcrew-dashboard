import { useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';

import { useFleet, type FleetNode, type OperationalIncident } from '@/core/fleet';
import { formatBytes, formatCpuCores, formatTime } from '@/core/formatting/formatters';
import { EmptyState } from '@/core/ui/EmptyState';
import { FilterToolbar } from '@/core/ui/FilterToolbar';
import { FormField } from '@/core/ui/FormField';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalTable } from '@/core/ui/OperationalTable';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { typography } from '@/core/ui/typography';
import { cn } from '@/lib/utils';

import {
  aggregateNode,
  getNodeStatus,
  selectNodes,
  type NodeSort,
  type NodeStatusFilter,
} from './nodeSummary';
import { HardwareComparison } from './components/HostHardwareSummary';
import { ActiveIncidentSummary } from './components/ActiveIncidentSummary';

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
  readonly incidents: ReadonlyArray<OperationalIncident>;
}

function NodeSummaryRow({
  node,
  tenantId,
  density,
  selected,
  onSelectionChanged,
  incidents,
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
          className="font-semibold text-link underline-offset-4 hover:underline"
          to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(node.nodeId)}`}
        >
          {node.displayName}
        </Link>
        <div className="mt-1 font-mono text-xs text-muted-foreground">{node.nodeId}</div>
      </td>
      <td className="px-4 py-2">
        <div className="flex flex-wrap gap-2">
          <StatusBadge status={status} />
          {incidents.some((incident) => incident.severity === 'critical') ? (
            <StatusBadge status="critical" />
          ) : incidents.length > 0 ? (
            <StatusBadge status="warning" />
          ) : null}
        </div>
        {!node.isOnline ? (
          <div className="mt-1 text-xs text-muted-foreground">
            {node.connectorHealth?.snapshot.lastFailureCategory
              ? `Retained cause: ${node.connectorHealth.snapshot.lastFailureCategory}`
              : 'Reason unavailable: no connector health replay'}
          </div>
        ) : null}
      </td>
      <td className="px-4 py-2">
        <LastKnownValue node={node}>{node.connectorVersion || 'Unknown'}</LastKnownValue>
      </td>
      <td className="px-4 py-2">{formatTime(node.lastSeenAt)}</td>
      <td className="px-4 py-2 text-right tabular-nums">
        <LastKnownValue node={node}>{node.profiles.length}</LastKnownValue>
      </td>
      <td className="px-4 py-2 text-right tabular-nums">
        <LastKnownValue node={node}>
          {aggregate.configuredSlots} / {aggregate.activeSlots} /{' '}
          {aggregate.eligibleSlots ?? 'Unknown'}
        </LastKnownValue>
      </td>
      <td className="px-4 py-2 text-right tabular-nums">
        {resources.reportingSources > 0 ? (
          <LastKnownValue node={node}>
            <div className="grid justify-items-end gap-1">
              <span>
                {formatCpuCores(resources.cpuCores)} /{' '}
                {formatBytes(resources.memoryWorkingSetBytes)}
              </span>
              <span className="flex items-center gap-2 text-xs text-muted-foreground">
                {resources.reportingSources} of {resources.totalSources} sources
                <StatusBadge status={resources.status} />
              </span>
            </div>
          </LastKnownValue>
        ) : (
          <div className="grid gap-1 text-muted-foreground">
            <span>Unavailable</span>
            {!node.isOnline ? <span className="text-xs">No last-known resource sample</span> : null}
          </div>
        )}
      </td>
    </tr>
  );
}

function LastKnownValue({
  node,
  children,
}: {
  readonly node: FleetNode;
  readonly children: ReactNode;
}) {
  return (
    <div className="grid gap-1">
      <div>{children}</div>
      {!node.isOnline ? (
        <div className="text-xs text-muted-foreground">
          Last known {formatTime(node.lastSeenAt)}
        </div>
      ) : null}
    </div>
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
      <section className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className={typography.sectionHeading}>Fleet status</h2>
          <p className={typography.metadata}>Node-level capacity and health across this tenant.</p>
        </div>
        <div className={cn('text-right', typography.metadata)}>
          {fleet ? `Updated ${formatTime(fleet.generatedAt)}` : 'Waiting for status'}
        </div>
      </section>

      {error ? (
        <StateBanner role={fleet ? 'status' : 'alert'} tone="caution">
          {fleet ? `Showing stale fleet data. ${error}` : error}
        </StateBanner>
      ) : null}

      <ActiveIncidentSummary
        incidents={fleet?.activeIncidents ?? []}
        tenantId={tenantId}
        testId="fleet-active-incidents"
      />

      {isLoading && !fleet ? <LoadingState label="Loading fleet status…" /> : null}

      {!isLoading && fleet?.nodes.length === 0 ? (
        <EmptyState
          description="Create a one-time code, configure it on a connector, and start the connector."
          title="No servers enrolled"
        />
      ) : null}

      {fleet && fleet.nodes.length > 0 ? (
        <>
          <FilterToolbar>
            <FormField label="Search nodes">
              <input
                className="h-9 rounded-md border bg-background px-3 text-sm"
                type="search"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
              />
            </FormField>
            <FormField label="Status">
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
            </FormField>
            <FormField label="Sort by">
              <select
                className="h-9 rounded-md border bg-background px-3 text-sm"
                value={sort}
                onChange={(event) => setSort(event.target.value as NodeSort)}
              >
                <option value="name">Display name</option>
                <option value="status">Status</option>
                <option value="lastSeen">Last seen</option>
              </select>
            </FormField>
            <FormField label="Density">
              <select
                className="h-9 rounded-md border bg-background px-3 text-sm"
                value={density}
                onChange={(event) => changeDensity(event.target.value as FleetDensity)}
              >
                <option value="comfortable">Comfortable</option>
                <option value="compact">Compact</option>
              </select>
            </FormField>
          </FilterToolbar>

          <HardwareComparison nodes={selectedNodes} />

          {nodes.length === 0 ? (
            <EmptyState
              description="Adjust the status or text filter to see more nodes."
              title="No matching nodes"
            />
          ) : (
            <>
              {/* Mobile summary cards */}
              <div className="grid gap-3 lg:hidden" data-testid="fleet-mobile-summary">
                {nodes.map((node) => {
                  const aggregate = aggregateNode(node);
                  const nodeStatus = getNodeStatus(node);
                  return (
                    <div
                      key={node.nodeId}
                      className="grid gap-2 rounded-lg border bg-card p-4"
                      data-testid={`fleet-node-card-${node.nodeId}`}
                    >
                      <div className="flex min-w-0 flex-wrap items-center gap-2">
                        <StatusBadge status={nodeStatus} />
                        <Link
                          className="min-w-0 break-words font-semibold text-link underline-offset-4 hover:underline"
                          to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(node.nodeId)}`}
                        >
                          {node.displayName}
                        </Link>
                      </div>
                      <div className="grid grid-cols-2 gap-2 text-xs text-muted-foreground">
                        <span>Profiles: {node.profiles.length}</span>
                        <span>
                          Slots: {aggregate.configuredSlots} / {aggregate.activeSlots}
                        </span>
                      </div>
                      <div className="text-xs text-muted-foreground">
                        Last seen {formatTime(node.lastSeenAt)}
                      </div>
                    </div>
                  );
                })}
              </div>

              {/* Desktop full evidence table */}
              <div className="hidden min-w-0 lg:block">
                <OperationalTable
                  caption="Fleet nodes for the active tenant"
                  columns={[
                    { key: 'node', header: 'Node' },
                    { key: 'state', header: 'State' },
                    { key: 'connector', header: 'Connector' },
                    { key: 'lastSeen', header: 'Last seen' },
                    { key: 'profiles', header: 'Profiles', align: 'right' },
                    {
                      key: 'slots',
                      header: 'Configured / local / GitHub eligible',
                      align: 'right',
                    },
                    { key: 'resources', header: 'CPU / memory evidence', align: 'right' },
                  ]}
                >
                  {nodes.map((node) => (
                    <NodeSummaryRow
                      key={node.nodeId}
                      node={node}
                      tenantId={tenantId}
                      density={density}
                      selected={selectedNodeIds.includes(node.nodeId)}
                      onSelectionChanged={changeSelection}
                      incidents={fleet.activeIncidents.filter(
                        (incident) => incident.nodeId === node.nodeId,
                      )}
                    />
                  ))}
                </OperationalTable>
              </div>
            </>
          )}
        </>
      ) : null}
    </>
  );
}
