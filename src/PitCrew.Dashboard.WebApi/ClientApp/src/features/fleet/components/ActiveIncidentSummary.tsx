import { Link } from 'react-router-dom';

import type { OperationalIncident } from '@/core/fleet';
import { StatusBadge } from '@/core/ui/StatusBadge';

interface ActiveIncidentSummaryProps {
  readonly tenantId: string;
  readonly incidents: ReadonlyArray<OperationalIncident>;
  readonly testId: string;
}

/** Renders compact active-incident severity without hiding the durable incidents page. */
export function ActiveIncidentSummary({ tenantId, incidents, testId }: ActiveIncidentSummaryProps) {
  if (incidents.length === 0) return null;
  const critical = incidents.filter((incident) => incident.severity === 'critical').length;
  const warning = incidents.length - critical;
  return (
    <div
      className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-amber-300 bg-amber-50 px-4 py-3 text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
      data-testid={testId}
      role="status"
    >
      <div className="flex flex-wrap items-center gap-2">
        {critical > 0 ? <StatusBadge status="critical" /> : <StatusBadge status="warning" />}
        <span className="font-semibold">
          {incidents.length} active {incidents.length === 1 ? 'incident' : 'incidents'}
        </span>
        <span className="text-sm">
          {critical} critical · {warning} warning
        </span>
      </div>
      <Link
        className="text-sm font-semibold text-primary underline-offset-4 hover:underline"
        to={`/tenants/${encodeURIComponent(tenantId)}/incidents`}
      >
        View incidents
      </Link>
    </div>
  );
}
