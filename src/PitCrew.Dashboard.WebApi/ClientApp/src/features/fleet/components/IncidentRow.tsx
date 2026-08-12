import { Link } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import type { FleetNode } from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import type { OperationalIncident } from '../incidentsApi';

interface IncidentRowProps {
  readonly incident: OperationalIncident;
  readonly canAcknowledge: boolean;
  readonly isAcknowledging: boolean;
  readonly onAcknowledge: (incident: OperationalIncident) => void;
  readonly onUnacknowledge: (incident: OperationalIncident) => void;
  readonly node: FleetNode | undefined;
}

export function IncidentRow({
  incident,
  canAcknowledge,
  isAcknowledging,
  onAcknowledge,
  onUnacknowledge,
  node,
}: IncidentRowProps) {
  const health = node?.connectorHealth?.snapshot;
  return (
    <tr className="border-t align-top" data-testid={`incident-row-${incident.incidentId}`}>
      <td className="px-4 py-3">
        <div className="flex flex-wrap gap-2">
          <StatusBadge status={incident.severity} />
          <StatusBadge status={incident.status} />
        </div>
      </td>
      <td className="px-4 py-3">
        <Link
          className="font-semibold text-link underline-offset-4 hover:underline"
          to={incident.link}
        >
          {incident.title}
        </Link>
        <p className="mt-1 max-w-3xl text-xs text-muted-foreground">{incident.summary}</p>
        {incident.evidence ? (
          <p className="mt-1 max-w-3xl text-xs text-muted-foreground">
            Evidence: {incident.evidence}
          </p>
        ) : null}
        {health && incident.kind === 'connector-offline' ? (
          <p className="mt-2 max-w-3xl rounded border bg-muted/30 px-2 py-1 text-xs">
            Retained connector evidence: {health.lastFailureCategory ?? 'category unavailable'};{' '}
            {health.activeOutageId
              ? `active since ${formatTime(health.activeOutageStartedAt)}`
              : health.lastRecoveredOutageId
                ? `recovered ${formatTime(health.lastRecoveredOutageStartedAt)} to ${formatTime(health.lastRecoveredAt)}`
                : 'no outage interval retained'}
            {health.nextRetryAt ? `; retry ${formatTime(health.nextRetryAt)}` : ''}.
          </p>
        ) : incident.kind === 'connector-offline' ? (
          <p className="mt-2 max-w-3xl rounded border bg-muted/30 px-2 py-1 text-xs">
            Reason unavailable: the unreachable connector has never replayed bounded health
            evidence.
          </p>
        ) : null}
      </td>
      <td className="px-4 py-3 font-mono text-xs">{incident.reason}</td>
      <td className="px-4 py-3 text-xs whitespace-nowrap">
        <div>Triggered {formatTime(incident.triggeredAt)}</div>
        <div className="mt-1 text-muted-foreground">
          Last observed {formatTime(incident.lastObservedAt)}
        </div>
        {incident.resolvedAt ? (
          <div className="mt-1 text-muted-foreground">
            Resolved {formatTime(incident.resolvedAt)}
          </div>
        ) : null}
      </td>
      <td className="px-4 py-3">
        {canAcknowledge && incident.status === 'triggered' ? (
          <div className="grid gap-1">
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={isAcknowledging}
              onClick={() => onAcknowledge(incident)}
            >
              {isAcknowledging ? 'Acknowledging…' : 'Acknowledge'}
            </Button>
            <span className="max-w-48 text-xs text-muted-foreground">
              Records operator ownership without resolving the condition. Reversible while active.
            </span>
          </div>
        ) : canAcknowledge && incident.status === 'acknowledged' ? (
          <div className="grid gap-1">
            <span className="text-xs text-muted-foreground">
              Acknowledged {formatTime(incident.acknowledgedAt)}
            </span>
            <Button
              type="button"
              size="sm"
              variant="ghost"
              disabled={isAcknowledging}
              onClick={() => onUnacknowledge(incident)}
            >
              {isAcknowledging ? 'Reverting…' : 'Unacknowledge'}
            </Button>
            <span className="max-w-48 text-xs text-muted-foreground">
              Returns this active incident to triggered.
            </span>
          </div>
        ) : incident.status === 'acknowledged' ? (
          <span className="text-xs text-muted-foreground">
            Acknowledged {formatTime(incident.acknowledgedAt)}
          </span>
        ) : (
          <span className="text-xs text-muted-foreground">No action</span>
        )}
      </td>
    </tr>
  );
}
