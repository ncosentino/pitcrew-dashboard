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
import {
  summarizeProfileAttention,
  type ProfileAttentionSummary,
  type ProfileAttentionTask,
} from './profileWorkspace';
import { HardwareComparison } from './components/HostHardwareSummary';
import { ActiveIncidentSummary } from './components/ActiveIncidentSummary';
import { incidentConditionLabel, isActionableIncident } from './incidentView';

type FleetDensity = 'comfortable' | 'compact';
type FleetSort = NodeSort | 'attention';

interface NodeProfileAttention {
  readonly profileId: string;
  readonly summary: ProfileAttentionSummary;
}

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
  readonly profileAttention?: NodeProfileAttention;
}

function NodeSummaryRow({
  node,
  tenantId,
  density,
  selected,
  onSelectionChanged,
  incidents,
  profileAttention,
}: NodeSummaryRowProps) {
  const aggregate = aggregateNode(node);
  const resources = aggregate.resources;
  const admission = summarizeNodeHostAdmission(node.profiles);
  const reporting = nodeReportingState(node);
  const incidentSignal = nodeIncidentSignal(incidents);
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
          <StatusBadge status={reporting.label} tone={reporting.tone} />
          {incidentSignal ? (
            <StatusBadge status={incidentSignal.label} tone={incidentSignal.tone} />
          ) : null}
          {nodeHasDegradedConnector(node) ? <StatusBadge status="degraded" /> : null}
          {profileAttention ? (
            <StatusBadge
              status={profileAttention.summary.label}
              tone={profileAttention.summary.tone}
            />
          ) : null}
        </div>
        {incidents.length > 0 ? (
          <Link
            className="mt-1 inline-block text-xs font-semibold text-link underline-offset-4 hover:underline"
            to={incidentQueueHref(tenantId, node.nodeId)}
          >
            Review {incidents.length} open incident {incidents.length === 1 ? 'record' : 'records'}
          </Link>
        ) : null}
        {profileAttention ? (
          <Link
            className="mt-1 block text-xs font-semibold text-link underline-offset-4 hover:underline"
            to={profileAttentionHref(
              tenantId,
              node.nodeId,
              profileAttention.profileId,
              profileAttention.summary.task,
            )}
          >
            Review {profileAttention.summary.label}
          </Link>
        ) : null}
        {!node.isOnline && !node.isRevoked ? (
          <div className="mt-1 text-xs text-muted-foreground">
            Reporting loss does not confirm host failure.{' '}
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
  const profileAttentionByNode = new Map<string, NodeProfileAttention>();
  for (const node of fleet?.nodes ?? []) {
    const profileAttention = selectNodeProfileAttention(node);
    if (profileAttention) profileAttentionByNode.set(node.nodeId, profileAttention);
  }
  const nodes =
    sort === 'attention'
      ? [...selectedNodes].sort((left, right) => {
          const rankDifference =
            nodeAttentionRank(
              left,
              incidentsByNode.get(left.nodeId) ?? [],
              profileAttentionByNode.get(left.nodeId),
            ) -
            nodeAttentionRank(
              right,
              incidentsByNode.get(right.nodeId) ?? [],
              profileAttentionByNode.get(right.nodeId),
            );
          if (rankDifference !== 0) return rankDifference;
          return 0;
        })
      : selectedNodes;
  const visibleNodes = nodes.slice(0, visibleLimit);
  const comparisonNodes = (fleet?.nodes ?? []).filter((node) =>
    selectedNodeIds.includes(node.nodeId),
  );
  const criticalIncidents = fleet?.activeCriticalIncidentTotal;
  const incidentTotal = fleet?.activeIncidentTotal;
  const actionableIncidents = activeIncidents.filter(isActionableIncident);
  const actionableCritical = actionableIncidents.filter(
    (incident) => incident.currentSeverity === 'critical',
  ).length;
  const actionableWarning = actionableIncidents.filter(
    (incident) => incident.currentSeverity === 'warning',
  ).length;
  const warningIncidents = fleet?.activeIncidents.filter(
    (incident) => incident.currentSeverity === 'warning',
  ).length;
  const conditionCounts = countIncidentConditions(activeIncidents);
  const reportingNodes =
    fleet?.nodes.filter((node) => getNodeStatus(node) === 'online').length ?? 0;
  const actionableProblemNodes =
    fleet?.nodes.filter(
      (node) =>
        (incidentsByNode.get(node.nodeId) ?? []).some(isActionableIncident) ||
        profileAttentionByNode.get(node.nodeId)?.summary.categories.includes('confirmed-problem'),
    ).length ?? 0;
  const activeChangeNodes =
    fleet?.nodes.filter((node) =>
      profileAttentionByNode.get(node.nodeId)?.summary.categories.includes('active-change'),
    ).length ?? 0;
  const evidenceGapNodes =
    fleet?.nodes.filter(
      (node) =>
        getNodeStatus(node) === 'offline' ||
        nodeHasDegradedConnector(node) ||
        profileAttentionByNode.get(node.nodeId)?.summary.categories.includes('evidence-gap'),
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
        description="Confirmed incidents lead. Connector reporting gaps, retained evidence, and node inventory remain distinct and subordinate."
        status={
          <StatusBadge
            status={
              !fleet
                ? error
                  ? 'Status unavailable'
                  : 'Loading'
                : criticalIncidents == null || incidentTotal == null
                  ? 'Incident count unavailable'
                  : actionableCritical > 0
                    ? 'Critical action required'
                    : actionableWarning > 0
                      ? 'Action required'
                      : actionableProblemNodes > 0
                        ? 'Profile action required'
                        : activeChangeNodes > 0
                          ? 'Changes in progress'
                          : evidenceGapNodes > 0
                            ? 'Evidence gaps'
                            : incidentTotal > 0
                              ? 'Open records'
                              : 'No reported exception'
            }
            tone={
              !fleet
                ? error
                  ? 'critical'
                  : 'neutral'
                : criticalIncidents == null || incidentTotal == null
                  ? 'neutral'
                  : actionableCritical > 0
                    ? 'critical'
                    : actionableWarning > 0 || actionableProblemNodes > 0 || activeChangeNodes > 0
                      ? 'caution'
                      : evidenceGapNodes > 0 || incidentTotal > 0
                        ? 'neutral'
                        : 'positive'
            }
          />
        }
        items={[
          {
            label: 'Response generated',
            value: fleet ? formatTime(fleet.generatedAt) : error ? 'Unavailable' : 'Loading…',
            detail: fleet
              ? 'Source observations retain their own timestamps'
              : 'Waiting for fleet evidence',
          },
          {
            label: 'Nodes reporting',
            value: fleet
              ? `${reportingNodes} of ${fleet.nodes.length}`
              : error
                ? 'Unavailable'
                : 'Loading…',
            detail: 'Current connector contact; not a host-liveness claim',
          },
          {
            label: 'Nodes requiring action',
            value: fleet ? actionableProblemNodes : error ? 'Unavailable' : 'Loading…',
            detail: 'Confirmed unowned incident or current profile exception',
          },
          {
            label: 'Active changes',
            value: fleet ? activeChangeNodes : error ? 'Unavailable' : 'Loading…',
            detail: 'Manager lifecycle or worker rollout is changing',
          },
          {
            label: 'Evidence gaps',
            value: fleet ? evidenceGapNodes : error ? 'Unavailable' : 'Loading…',
            detail: 'Connector reporting or profile evidence is unavailable, stale, or partial',
          },
          {
            label: 'Open incident records',
            value: fleet ? (incidentTotal ?? 'Unavailable') : error ? 'Unavailable' : 'Loading…',
            detail: fleet
              ? criticalIncidents == null || incidentTotal == null
                ? 'Authoritative incident totals unavailable'
                : fleet.activeIncidentsTruncated === false
                  ? `${criticalIncidents} current critical · ${warningIncidents} current warning · ${formatConditionCounts(conditionCounts)}`
                  : `${criticalIncidents} current critical across all pages · loaded conditions: ${formatConditionCounts(conditionCounts)} · remaining breakdown unavailable`
              : 'Incident evidence unavailable',
          },
        ]}
      />

      {error ? (
        <StateBanner role={fleet ? 'status' : 'alert'} tone="caution">
          {fleet ? `Showing stale fleet data. ${error}` : error}
        </StateBanner>
      ) : null}

      {fleet?.activeIncidentsTruncated ? (
        <StateBanner role="status" tone="caution">
          Fleet rows include {activeIncidents.length} of {fleet.activeIncidentTotal} open incident
          records. Open the incident queue for exact totals, pagination, and deep-linked history.
        </StateBanner>
      ) : null}

      <ActiveIncidentSummary
        incidents={activeIncidents}
        tenantId={tenantId}
        testId="fleet-active-incidents"
        totalCount={fleet?.activeIncidentTotal}
        criticalCount={fleet?.activeCriticalIncidentTotal}
        truncated={fleet?.activeIncidentsTruncated}
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
              Retained evidence from before its connector stopped reporting.
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

      {isLoading && !fleet ? <LoadingState label="Loading fleet status…" /> : null}

      {!isLoading && fleet?.nodes.length === 0 ? (
        <EmptyState
          description="Create a one-time code, configure it on a connector, and start the connector. No enrolled nodes means no connector has reported; it does not prove fleet health."
          title="No nodes enrolled"
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
                <option value="online">Connector reporting</option>
                <option value="offline">Connector not reporting</option>
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
                  const reporting = nodeReportingState(node);
                  const admission = summarizeNodeHostAdmission(node.profiles);
                  const nodeIncidents = incidentsByNode.get(node.nodeId) ?? [];
                  const incidentSignal = nodeIncidentSignal(nodeIncidents);
                  const profileAttention = profileAttentionByNode.get(node.nodeId);
                  return (
                    <div
                      key={node.nodeId}
                      className="grid gap-2 rounded-lg border bg-card p-4"
                      data-testid={`fleet-node-card-${node.nodeId}`}
                    >
                      <div className="flex min-w-0 flex-wrap items-center gap-2">
                        <StatusBadge status={reporting.label} tone={reporting.tone} />
                        {incidentSignal ? (
                          <StatusBadge status={incidentSignal.label} tone={incidentSignal.tone} />
                        ) : null}
                        {nodeHasDegradedConnector(node) ? <StatusBadge status="degraded" /> : null}
                        {profileAttention ? (
                          <StatusBadge
                            status={profileAttention.summary.label}
                            tone={profileAttention.summary.tone}
                          />
                        ) : null}
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
                          to={incidentQueueHref(tenantId, node.nodeId)}
                        >
                          Review {nodeIncidents.length} open incident{' '}
                          {nodeIncidents.length === 1 ? 'record' : 'records'}
                        </Link>
                      ) : null}
                      {profileAttention ? (
                        <Link
                          className="text-xs font-semibold text-link underline-offset-4 hover:underline"
                          to={profileAttentionHref(
                            tenantId,
                            node.nodeId,
                            profileAttention.profileId,
                            profileAttention.summary.task,
                          )}
                        >
                          Review {profileAttention.summary.label}
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
                      profileAttention={profileAttentionByNode.get(node.nodeId)}
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

function nodeAttentionRank(
  node: FleetNode,
  incidents: ReadonlyArray<OperationalIncident>,
  profileAttention: NodeProfileAttention | undefined,
): number {
  const actionableIncidents = incidents.filter(isActionableIncident);
  if (actionableIncidents.some((incident) => incident.currentSeverity === 'critical')) return 0;
  if (actionableIncidents.some((incident) => incident.currentSeverity === 'warning')) return 1;
  if (profileAttention?.summary.categories.includes('confirmed-problem')) return 2;
  if (profileAttention?.summary.categories.includes('active-change')) return 3;
  if (
    nodeHasDegradedConnector(node) ||
    profileAttention?.summary.categories.includes('evidence-gap')
  ) {
    return 4;
  }
  if (getNodeStatus(node) === 'offline') return 5;
  if (profileAttention || incidents.length > 0) return 6;
  if (getNodeStatus(node) === 'online') return 7;
  return 8;
}

function incidentQueueHref(tenantId: string, nodeId: string): string {
  const query = new URLSearchParams({ view: 'active', nodeId });
  return `/tenants/${encodeURIComponent(tenantId)}/incidents?${query.toString()}`;
}

function nodeHasDegradedConnector(node: FleetNode): boolean {
  return node.isOnline && !node.isRevoked && node.connectorHealth?.snapshot.state === 'degraded';
}

function nodeReportingState(node: FleetNode): {
  readonly label: string;
  readonly tone: 'positive' | 'caution' | 'neutral';
} {
  if (node.isRevoked) return { label: 'Enrollment revoked', tone: 'neutral' };
  if (node.isOnline) return { label: 'Connector reporting', tone: 'positive' };
  return { label: 'Connector not reporting', tone: 'caution' };
}

function nodeIncidentSignal(
  incidents: ReadonlyArray<OperationalIncident>,
): { readonly label: string; readonly tone: 'critical' | 'caution' | 'neutral' } | null {
  const actionableIncidents = incidents.filter(isActionableIncident);
  if (actionableIncidents.some((incident) => incident.currentSeverity === 'critical')) {
    return { label: 'Critical action required', tone: 'critical' };
  }
  if (actionableIncidents.some((incident) => incident.currentSeverity === 'warning')) {
    return { label: 'Warning action required', tone: 'caution' };
  }
  if (incidents.some((incident) => incident.operatorState === 'acknowledged')) {
    return { label: 'Operator acknowledged', tone: 'caution' };
  }
  const condition = incidents[0]?.conditionState;
  return condition ? { label: incidentConditionLabel(condition), tone: 'neutral' } : null;
}

function countIncidentConditions(incidents: ReadonlyArray<OperationalIncident>) {
  return {
    waiting: incidents.filter((incident) => incident.conditionState === 'waiting-for-evidence')
      .length,
    recovering: incidents.filter((incident) => incident.conditionState === 'recovering').length,
    monitoringEnded: incidents.filter((incident) => incident.conditionState === 'monitoring-ended')
      .length,
    retained: incidents.filter((incident) =>
      ['legacy-unverified', 'resolved'].includes(incident.conditionState),
    ).length,
  };
}

function formatConditionCounts(counts: ReturnType<typeof countIncidentConditions>): string {
  return `${counts.waiting} waiting for evidence · ${counts.recovering} recovering · ${counts.monitoringEnded} monitoring ended · ${counts.retained} retained`;
}

function selectNodeProfileAttention(node: FleetNode): NodeProfileAttention | undefined {
  if (!node.isOnline || node.isRevoked) return undefined;
  return node.profiles
    .map((profile) => ({
      profileId: profile.profileId,
      summary: summarizeProfileAttention(profile, []),
    }))
    .filter((candidate) => candidate.summary.rank < 100)
    .sort((left, right) => left.summary.rank - right.summary.rank)[0];
}

function profileAttentionHref(
  tenantId: string,
  nodeId: string,
  profileId: string,
  task: ProfileAttentionTask,
): string {
  const basePath = `/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(nodeId)}/profiles/${encodeURIComponent(profileId)}`;
  return task === 'overview' ? basePath : `${basePath}/${task}`;
}
