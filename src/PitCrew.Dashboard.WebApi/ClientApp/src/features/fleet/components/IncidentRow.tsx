import { Link } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import type { FleetNode } from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { OperationalRow } from '@/core/ui/OperationalList';
import { StatusBadge } from '@/core/ui/StatusBadge';

import type { OperationalIncident } from '../incidentsApi';
import { incidentConditionLabel } from '../incidentView';
import type { IncidentEnrichmentStatus } from './IncidentDetail';

interface IncidentRowProps {
  readonly incident: OperationalIncident;
  readonly node: FleetNode | undefined;
  readonly enrichmentStatus: IncidentEnrichmentStatus;
  readonly selectionHref: string;
  readonly selected: boolean;
  readonly onSelect: (incidentId: string) => void;
}

export function IncidentRow({
  incident,
  node,
  enrichmentStatus,
  selectionHref,
  selected,
  onSelect,
}: IncidentRowProps) {
  const nodeLabel = node
    ? node.displayName
    : enrichmentStatus === 'loading'
      ? 'Node identity loading…'
      : enrichmentStatus === 'unavailable'
        ? 'Node identity unavailable'
        : 'Node not present';
  const conditionLabel = incidentConditionLabel(incident.conditionState);
  return (
    <OperationalRow
      testId={`incident-row-${incident.incidentId}`}
      selected={selected}
      title={incident.title}
      description={incident.summary}
      status={
        <>
          {incident.currentSeverity ? <StatusBadge status={incident.currentSeverity} /> : null}
          <StatusBadge status={conditionLabel} tone="neutral" />
          {incident.operatorState === 'acknowledged' ? (
            <StatusBadge status="Operator acknowledged" tone="caution" />
          ) : null}
        </>
      }
      metadata={
        <div className="flex min-w-0 flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
          <span className="[overflow-wrap:anywhere]">{nodeLabel}</span>
          <span className="[overflow-wrap:anywhere]">
            {incident.profileId ? `Profile ${incident.profileId}` : 'Node scope'}
          </span>
          <span>
            {incident.currentSeverity
              ? `Confirmed ${incident.currentSeverity} problem`
              : `Last confirmed ${incident.lastConfirmedSeverity}; current impact unavailable`}
          </span>
          <span>Evaluated {formatTime(incident.evaluatedAt ?? incident.lastObservedAt)}</span>
        </div>
      }
      actions={
        <Button asChild size="sm" variant={selected ? 'secondary' : 'outline'}>
          <Link
            aria-current={selected ? 'page' : undefined}
            to={selectionHref}
            onClick={() => onSelect(incident.incidentId)}
          >
            {selected ? 'Selected' : 'Investigate'}
          </Link>
        </Button>
      }
    />
  );
}
