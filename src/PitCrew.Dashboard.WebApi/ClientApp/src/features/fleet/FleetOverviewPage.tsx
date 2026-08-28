import { useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import {
  summarizeNodeHostAdmission,
  useFleet,
  type FleetNode,
  type OperationalIncident,
} from '@/core/fleet';
import {
  formatBytes,
  formatCounter,
  formatCpuCores,
  formatTime,
} from '@/core/formatting/formatters';
import { EmptyState } from '@/core/ui/EmptyState';
import { FilterToolbar } from '@/core/ui/FilterToolbar';
import { FormField } from '@/core/ui/FormField';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalTable } from '@/core/ui/OperationalTable';
import { ReadinessSummary } from '@/core/ui/ReadinessSummary';
import { StateBanner } from '@/core/ui/StateBanner';
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
import { ActiveIncidentSummary } from './components/ActiveIncidentSummary';

type FleetDensity = 'comfortable' | 'compact';
type FleetSort = NodeSort | 'attention';

/** Browser storage key for the fleet overview density preference. */
export const fleetDensityStorageKey = 'pitcrew-dashboard-fleet-density';
/** Maximum fleet nodes rendered before the operator deliberately reveals another page. */
export const fleetPageSize = 100;

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
  const admission = summarizeNodeHostAdmission(node.profiles);
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
          {nodeHasDegradedConnector(node) ? <StatusBadge status="degraded" /> : null}
        </div>
        {incidents.length > 0 ? (
          <Link
            className="mt-1 inline-block text-xs font-semibold text-link underline-offset-4 hover:underline"
            to={incidentInvestigationHref(tenantId, incidents[0].incidentId)}
          >
            Review {incidents.length} active {incidents.length === 1 ? 'incident' : 'incidents'}
          </Link>
        ) : null}
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
          <div className="grid justify-items-end gap-0.5 text-xs">
            <span>
              <span className="text-muted-foreground">Configured</span> {aggregate.configuredSlots}
            </span>
            <span>
              <span className="text-muted-foreground">Local</span> {aggregate.activeSlots}
            </span>
            <span>
              <span className="text-muted-foreground">Eligible</span>{' '}
              {aggregate.eligibleSlots ?? 'Unknown'}
            </span>
          </div>
        </LastKnownValue>
      </td>
      <td className="px-4 py-2">
        <LastKnownValue node={node}>
          <div className="grid gap-1">
            <StatusBadge status={admission.status} />
            <span className="text-xs text-muted-foreground">
              {admission.status === 'disabled'
                ? 'Not configured'
                : admission.borrowedUnits == null || admission.withheldUnits == null
                  ? 'Accounting unavailable'
                  : `${formatCounter(admission.withheldUnits)} withheld · ${formatCounter(admission.borrowedUnits)} borrowed`}
            </span>
          </div>
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
  const [sort, setSort] = useState<FleetSort>('attention');
  const [density, setDensity] = useState<FleetDensity>(readDensity);
  const [visibleLimit, setVisibleLimit] = useState(fleetPageSize);
  const [selectedNodeIds, setSelectedNodeIds] = useState<readonly string[]>([]);
  const selectedNodes = selectNodes(
    fleet?.nodes ?? [],
    status,
    query,
    sort === 'attention' ? 'name' : sort,
  );
  const activeIncidents = fleet?.activeIncidents ?? [];
  const incidentsByNode = new Map<string, OperationalIncident[]>();
  for (const incident of activeIncidents) {
    const current = incidentsByNode.get(incident.nodeId);
    if (current) current.push(incident);
    else incidentsByNode.set(incident.nodeId, [incident]);
  }
  const nodes =
    sort === 'attention'
      ? [...selectedNodes].sort((left, right) => {
          const rankDifference =
            nodeAttentionRank(left, incidentsByNode.get(left.nodeId) ?? []) -
            nodeAttentionRank(right, incidentsByNode.get(right.nodeId) ?? []);
          if (rankDifference !== 0) return rankDifference;
          return 0;
        })
      : selectedNodes;
  const visibleNodes = nodes.slice(0, visibleLimit);
  const comparisonNodes = (fleet?.nodes ?? []).filter((node) =>
    selectedNodeIds.includes(node.nodeId),
  );
  const criticalIncidents = activeIncidents.filter(
    (incident) => incident.severity === 'critical',
  ).length;
  const warningIncidents = activeIncidents.length - criticalIncidents;
  const onlineNodes = fleet?.nodes.filter((node) => getNodeStatus(node) === 'online').length ?? 0;
  const attentionNodes =
    fleet?.nodes.filter(
      (node) =>
        getNodeStatus(node) === 'offline' ||
        nodeHasDegradedConnector(node) ||
        (incidentsByNode.get(node.nodeId)?.length ?? 0) > 0,
    ).length ?? 0;

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
      <ReadinessSummary
        title="Fleet readiness"
        description="Current or retained node evidence, active incidents, and the fleet records that require operator attention."
        status={
          <StatusBadge
            status={
              !fleet
                ? error
                  ? 'Status unavailable'
                  : 'Loading'
                : criticalIncidents > 0
                  ? 'Critical incidents'
                  : warningIncidents > 0 || attentionNodes > 0
                    ? 'Needs attention'
                    : 'No reported exception'
            }
            tone={
              !fleet
                ? error
                  ? 'critical'
                  : 'neutral'
                : criticalIncidents > 0
                  ? 'critical'
                  : warningIncidents > 0 || attentionNodes > 0
                    ? 'caution'
                    : 'positive'
            }
          />
        }
        items={[
          {
            label: 'Observation',
            value: fleet ? formatTime(fleet.generatedAt) : error ? 'Unavailable' : 'Loading…',
            detail: fleet ? 'Latest accepted tenant projection' : 'Waiting for fleet evidence',
          },
          {
            label: 'Nodes online',
            value: fleet
              ? `${onlineNodes} of ${fleet.nodes.length}`
              : error
                ? 'Unavailable'
                : 'Loading…',
            detail: 'Connector-derived node state',
          },
          {
            label: 'Nodes needing attention',
            value: fleet ? attentionNodes : error ? 'Unavailable' : 'Loading…',
            detail: 'Offline, connector-degraded, or named by an active incident',
          },
          {
            label: 'Active incidents',
            value: fleet ? activeIncidents.length : error ? 'Unavailable' : 'Loading…',
            detail: fleet
              ? `${criticalIncidents} critical · ${warningIncidents} warning`
              : 'Incident evidence unavailable',
          },
        ]}
      />

      <details className="rounded-lg border bg-card px-4 py-3">
        <summary className="cursor-pointer text-sm font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
          How to read fleet evidence
        </summary>
        <dl className="mt-3 grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-5">
          <div>
            <dt className="font-medium">Current</dt>
            <dd className="text-muted-foreground">Reported by the latest accepted observation.</dd>
          </div>
          <div>
            <dt className="font-medium">Last known</dt>
            <dd className="text-muted-foreground">
              Retained evidence from before a node went offline.
            </dd>
          </div>
          <div>
            <dt className="font-medium">Stale</dt>
            <dd className="text-muted-foreground">
              Available evidence that exceeded its freshness boundary.
            </dd>
          </div>
          <div>
            <dt className="font-medium">Unavailable</dt>
            <dd className="text-muted-foreground">No trustworthy measurement was reported.</dd>
          </div>
          <div>
            <dt className="font-medium">Acknowledged</dt>
            <dd className="text-muted-foreground">
              An operator owns the incident; the condition is not resolved.
            </dd>
          </div>
        </dl>
      </details>

      {error ? (
        <StateBanner role={fleet ? 'status' : 'alert'} tone="caution">
          {fleet ? `Showing stale fleet data. ${error}` : error}
        </StateBanner>
      ) : null}

      <ActiveIncidentSummary
        incidents={activeIncidents}
        tenantId={tenantId}
        testId="fleet-active-incidents"
      />

      {isLoading && !fleet ? <LoadingState label="Loading fleet status…" /> : null}

      {!isLoading && fleet?.nodes.length === 0 ? (
        <EmptyState
          description="Create a one-time code, configure it on a connector, and start the connector. No enrolled servers means no connector has reported; it does not prove fleet health."
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
                onChange={(event) => {
                  setQuery(event.target.value);
                  setVisibleLimit(fleetPageSize);
                }}
              />
            </FormField>
            <FormField label="Status">
              <select
                className="h-9 rounded-md border bg-background px-3 text-sm"
                value={status}
                onChange={(event) => {
                  setStatus(event.target.value as NodeStatusFilter);
                  setVisibleLimit(fleetPageSize);
                }}
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
                onChange={(event) => {
                  setSort(event.target.value as FleetSort);
                  setVisibleLimit(fleetPageSize);
                }}
              >
                <option value="attention">Attention first</option>
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

          <HardwareComparison nodes={comparisonNodes} />

          {nodes.length === 0 ? (
            <EmptyState
              description="No nodes match the current filter combination. This does not mean the fleet is empty."
              title="No matching nodes"
            />
          ) : (
            <>
              {/* Mobile summary cards */}
              <div className="grid gap-3 lg:hidden" data-testid="fleet-mobile-summary">
                {visibleNodes.map((node) => {
                  const aggregate = aggregateNode(node);
                  const nodeStatus = getNodeStatus(node);
                  const admission = summarizeNodeHostAdmission(node.profiles);
                  const nodeIncidents = incidentsByNode.get(node.nodeId) ?? [];
                  return (
                    <div
                      key={node.nodeId}
                      className="grid gap-2 rounded-lg border bg-card p-4"
                      data-testid={`fleet-node-card-${node.nodeId}`}
                    >
                      <div className="flex min-w-0 flex-wrap items-center gap-2">
                        <StatusBadge status={nodeStatus} />
                        {nodeIncidents.some((incident) => incident.severity === 'critical') ? (
                          <StatusBadge status="critical" />
                        ) : nodeIncidents.length > 0 ? (
                          <StatusBadge status="warning" />
                        ) : null}
                        {nodeHasDegradedConnector(node) ? <StatusBadge status="degraded" /> : null}
                        <Link
                          className="min-w-0 break-words font-semibold text-link underline-offset-4 hover:underline"
                          to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(node.nodeId)}`}
                        >
                          {node.displayName}
                        </Link>
                      </div>
                      {nodeIncidents.length > 0 ? (
                        <Link
                          className="text-xs font-semibold text-link underline-offset-4 hover:underline"
                          to={incidentInvestigationHref(tenantId, nodeIncidents[0].incidentId)}
                        >
                          Review {nodeIncidents.length} active{' '}
                          {nodeIncidents.length === 1 ? 'incident' : 'incidents'}
                        </Link>
                      ) : null}
                      <div className="grid grid-cols-2 gap-2 text-xs text-muted-foreground sm:grid-cols-4">
                        <span>Profiles: {node.profiles.length}</span>
                        <span>Configured: {aggregate.configuredSlots}</span>
                        <span>Local: {aggregate.activeSlots}</span>
                        <span>Eligible: {aggregate.eligibleSlots ?? 'Unknown'}</span>
                      </div>
                      <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                        <StatusBadge status={admission.status} />
                        <span>
                          Host admission:{' '}
                          {admission.status === 'disabled'
                            ? 'not configured'
                            : admission.withheldUnits == null
                              ? 'accounting unavailable'
                              : `${formatCounter(admission.withheldUnits)} withheld`}
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
                      header: 'Capacity evidence',
                      align: 'right',
                    },
                    { key: 'admission', header: 'Host admission' },
                    { key: 'resources', header: 'CPU / memory evidence', align: 'right' },
                  ]}
                >
                  {visibleNodes.map((node) => (
                    <NodeSummaryRow
                      key={node.nodeId}
                      node={node}
                      tenantId={tenantId}
                      density={density}
                      selected={selectedNodeIds.includes(node.nodeId)}
                      onSelectionChanged={changeSelection}
                      incidents={incidentsByNode.get(node.nodeId) ?? []}
                    />
                  ))}
                </OperationalTable>
              </div>
              <div className="flex flex-wrap items-center justify-between gap-3 text-sm">
                <span role="status">
                  Showing {visibleNodes.length} of {nodes.length}{' '}
                  {nodes.length === 1 ? 'node' : 'nodes'}
                </span>
                {visibleNodes.length < nodes.length ? (
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setVisibleLimit((current) => current + fleetPageSize)}
                  >
                    Show next {Math.min(fleetPageSize, nodes.length - visibleNodes.length)}
                  </Button>
                ) : null}
              </div>
            </>
          )}
        </>
      ) : null}
    </>
  );
}

function nodeAttentionRank(node: FleetNode, incidents: ReadonlyArray<OperationalIncident>): number {
  if (incidents.some((incident) => incident.severity === 'critical')) return 0;
  if (incidents.length > 0) return 1;
  if (nodeHasDegradedConnector(node)) return 2;
  if (getNodeStatus(node) === 'offline') return 3;
  if (getNodeStatus(node) === 'online') return 4;
  return 5;
}

function incidentInvestigationHref(tenantId: string, incidentId: string): string {
  return `/tenants/${encodeURIComponent(tenantId)}/incidents?view=active&incident=${encodeURIComponent(incidentId)}`;
}

function nodeHasDegradedConnector(node: FleetNode): boolean {
  return node.isOnline && !node.isRevoked && node.connectorHealth?.snapshot.state === 'degraded';
}
