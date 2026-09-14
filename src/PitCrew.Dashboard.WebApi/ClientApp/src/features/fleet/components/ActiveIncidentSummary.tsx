import { Link } from 'react-router-dom';

import type { OperationalIncident } from '@/core/fleet';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

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
  const visibleUnknown = incidents.length - visibleCritical - visibleWarning;
  const criticalIsAuthoritative = criticalCount != null || sliceIsComplete;
  const critical = criticalCount ?? visibleCritical;
  const tone = critical > 0 ? ('critical' as const) : ('caution' as const);
  const severitySummary =
    critical > 0
      ? 'highest severity critical'
      : sliceIsComplete && visibleWarning > 0
        ? 'highest severity warning'
        : 'current severity unavailable';
  const incidentLabel = `${total} active ${total === 1 ? 'incident' : 'incidents'}${!totalIsAuthoritative && !sliceIsComplete ? ' shown' : ''}`;
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
        {critical > 0 ? (
          <StatusBadge status="critical" />
        ) : visibleWarning > 0 ? (
          <StatusBadge status="warning" />
        ) : (
          <StatusBadge status="severity unavailable" tone="neutral" />
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
        {visibleUnknown > 0 ? (
          <span className="text-sm">
            {countLabel(visibleUnknown, 'awaiting evidence', sliceIsComplete)}
          </span>
        ) : null}
      </div>
      <Link
        aria-label={`Review ${incidentLabel}`}
        className="text-sm font-semibold underline-offset-4 hover:underline"
        to={incidentQueueHref(tenantId, nodeId, profileId)}
      >
        View incidents
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
