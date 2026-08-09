import { Link } from 'react-router-dom';

import type { OperationalIncident } from '@/core/fleet';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

interface ActiveIncidentSummaryProps {
  readonly tenantId: string;
  readonly incidents: ReadonlyArray<OperationalIncident>;
  readonly testId: string;
}

/** Renders compact active-incident severity above fleet inventory using shared primitives. */
export function ActiveIncidentSummary({ tenantId, incidents, testId }: ActiveIncidentSummaryProps) {
  if (incidents.length === 0) return null;
  const critical = incidents.filter((incident) => incident.severity === 'critical').length;
  const warning = incidents.length - critical;
  const tone = critical > 0 ? ('critical' as const) : ('caution' as const);
  return (
    <StateBanner
      tone={tone}
      role="status"
      className="flex flex-wrap items-center justify-between gap-3 px-4 py-3"
      data-testid={testId}
    >
      <div className="flex flex-wrap items-center gap-2">
        {critical > 0 ? <StatusBadge status="critical" /> : <StatusBadge status="warning" />}
        <span className="font-semibold">
          {incidents.length} active {incidents.length === 1 ? 'incident' : 'incidents'}
        </span>
        {critical > 0 ? <span className="text-sm">{critical} critical</span> : null}
        {warning > 0 ? <span className="text-sm">{warning} warning</span> : null}
      </div>
      <Link
        className="text-sm font-semibold underline-offset-4 hover:underline"
        to={`/tenants/${encodeURIComponent(tenantId)}/incidents`}
      >
        View incidents
      </Link>
    </StateBanner>
  );
}
