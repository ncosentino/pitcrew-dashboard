import { Link } from 'react-router-dom';

import type { OperationalIncident } from '@/core/fleet';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { isActionableIncident } from '../incidentView';

interface ActiveIncidentSummaryProps {
  readonly tenantId: string;
  readonly incidents: ReadonlyArray<OperationalIncident>;
  readonly testId: string;
  readonly totalCount?: number;
  readonly criticalCount?: number;
  readonly truncated?: boolean;
  readonly nodeId?: string;
  readonly profileId?: string;
}

/** Renders compact active-incident severity above fleet inventory using shared primitives. */
export function ActiveIncidentSummary({
  tenantId,
  incidents,
  testId,
  totalCount,
  criticalCount,
  truncated,
  nodeId,
  profileId,
}: ActiveIncidentSummaryProps) {
  const sliceIsComplete = truncated === false;
  const totalIsAuthoritative = totalCount != null;
  const total = totalCount ?? incidents.length;
  if (total === 0) return null;
  const visibleCritical = incidents.filter(
    (incident) => incident.currentSeverity === 'critical',
  ).length;
  const visibleWarning = incidents.filter(
    (incident) => incident.currentSeverity === 'warning',
  ).length;
  const visibleUnconfirmed = incidents.length - visibleCritical - visibleWarning;
  const actionableIncidents = incidents.filter(isActionableIncident);
  const actionableCritical = actionableIncidents.filter(
    (incident) => incident.currentSeverity === 'critical',
  ).length;
  const actionableWarning = actionableIncidents.filter(
    (incident) => incident.currentSeverity === 'warning',
  ).length;
  const criticalIsAuthoritative = criticalCount != null || sliceIsComplete;
  const critical = criticalCount ?? visibleCritical;
  const tone =
    actionableCritical > 0
      ? ('critical' as const)
      : actionableWarning > 0
        ? ('caution' as const)
        : ('neutral' as const);
  const severitySummary =
    actionableCritical > 0
      ? 'highest unowned current severity critical'
      : actionableWarning > 0
        ? 'highest unowned current severity warning'
        : incidents.some(
              (incident) =>
                incident.operatorState === 'acknowledged' && incident.currentSeverity != null,
            )
          ? 'current problem operator acknowledged'
          : 'current impact unavailable';
  const incidentLabel = `${total} open incident ${total === 1 ? 'record' : 'records'}${!totalIsAuthoritative && !sliceIsComplete ? ' shown' : ''}`;
  const countLabel = (count: number, label: string, authoritative: boolean) =>
    `${count} ${label}${authoritative ? '' : ' shown'}`;
  return (
    <StateBanner
      tone={tone}
      role="status"
      aria-label={`${incidentLabel}; ${severitySummary}`}
      className="flex flex-wrap items-center justify-between gap-3 px-4 py-3"
      data-testid={testId}
    >
      <div className="flex flex-wrap items-center gap-2">
        {actionableCritical > 0 ? (
          <StatusBadge status="critical" />
        ) : actionableWarning > 0 ? (
          <StatusBadge status="warning" />
        ) : (
          <StatusBadge status="Open records" tone="neutral" />
        )}
        <span className="font-semibold">{incidentLabel}</span>
        {critical > 0 ? (
          <span className="text-sm">
            {countLabel(critical, 'critical', criticalIsAuthoritative)}
          </span>
        ) : null}
        {visibleWarning > 0 ? (
          <span className="text-sm">{countLabel(visibleWarning, 'warning', sliceIsComplete)}</span>
        ) : null}
        {visibleUnconfirmed > 0 ? (
          <span className="text-sm">
            {countLabel(visibleUnconfirmed, 'without current severity', sliceIsComplete)}
          </span>
        ) : null}
      </div>
      <Link
        aria-label={`Review ${incidentLabel}`}
        className="text-sm font-semibold underline-offset-4 hover:underline"
        to={incidentQueueHref(tenantId, nodeId, profileId)}
      >
        Review incident queue
      </Link>
    </StateBanner>
  );
}

function incidentQueueHref(tenantId: string, nodeId?: string, profileId?: string): string {
  const query = new URLSearchParams({ view: 'active' });
  if (nodeId) query.set('nodeId', nodeId);
  if (profileId) query.set('profileId', profileId);
  return `/tenants/${encodeURIComponent(tenantId)}/incidents?${query.toString()}`;
}
