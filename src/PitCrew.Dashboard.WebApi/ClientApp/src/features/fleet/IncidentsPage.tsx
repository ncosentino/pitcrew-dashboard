import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';
import { getFleet, type FleetNode } from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import {
  acknowledgeIncident,
  getIncidents,
  type IncidentFilter,
  type IncidentPage,
  type OperationalIncident,
} from './incidentsApi';

interface IncidentRowProps {
  readonly incident: OperationalIncident;
  readonly canAcknowledge: boolean;
  readonly isAcknowledging: boolean;
  readonly onAcknowledge: (incident: OperationalIncident) => void;
  readonly node: FleetNode | undefined;
}

function IncidentRow({
  incident,
  canAcknowledge,
  isAcknowledging,
  onAcknowledge,
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
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={isAcknowledging}
            onClick={() => onAcknowledge(incident)}
          >
            {isAcknowledging ? 'Acknowledging…' : 'Acknowledge'}
          </Button>
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

/** Renders active incidents and bounded resolved history without crowding fleet status pages. */
export default function IncidentsPage() {
  const { tenantId = '' } = useParams();
  const { session } = useSession();
  const [nodes, setNodes] = useState<ReadonlyArray<FleetNode>>([]);
  const [filter, setFilter] = useState<IncidentFilter>('active');
  const [page, setPage] = useState<IncidentPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [acknowledgingId, setAcknowledgingId] = useState<string | null>(null);
  const requestVersion = useRef(0);
  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);
  const canAcknowledge = tenant?.role === 'administrator' || tenant?.role === 'owner';
  const antiforgeryToken = session?.antiforgeryToken ?? '';

  const load = useCallback(
    async (signal?: AbortSignal) => {
      const version = ++requestVersion.current;
      setIsLoading(true);
      try {
        const requestSignal = signal ?? new AbortController().signal;
        const fleetPromise = getFleet(tenantId, requestSignal).catch((caught: unknown) => {
          if (caught instanceof DOMException && caught.name === 'AbortError') return null;
          console.warn('Connector health evidence is unavailable on the incident page.', caught);
          return null;
        });
        const next = await getIncidents(tenantId, filter, signal);
        if (version !== requestVersion.current) return;
        setPage(next);
        setError(null);
        void fleetPromise.then((nextFleet) => {
          if (version === requestVersion.current) setNodes(nextFleet?.nodes ?? []);
        });
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === 'AbortError') return;
        if (version !== requestVersion.current) return;
        setError(caught instanceof Error ? caught.message : 'Incident history is unavailable.');
      } finally {
        if (!signal?.aborted && version === requestVersion.current) setIsLoading(false);
      }
    },
    [filter, tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    const initial = globalThis.setTimeout(() => {
      void load(controller.signal);
    }, 0);
    const refresh = globalThis.setInterval(() => void load(controller.signal), 30_000);
    return () => {
      controller.abort();
      globalThis.clearTimeout(initial);
      globalThis.clearInterval(refresh);
    };
  }, [load]);

  const counts = useMemo(
    () => ({
      critical: page?.incidents.filter((incident) => incident.severity === 'critical').length ?? 0,
      warning: page?.incidents.filter((incident) => incident.severity === 'warning').length ?? 0,
      acknowledged:
        page?.incidents.filter((incident) => incident.status === 'acknowledged').length ?? 0,
    }),
    [page],
  );

  const acknowledge = async (incident: OperationalIncident) => {
    setAcknowledgingId(incident.incidentId);
    setError(null);
    try {
      await acknowledgeIncident(tenantId, incident.incidentId, antiforgeryToken);
      await load();
    } catch (caught) {
      setError(
        caught instanceof Error ? caught.message : 'The incident could not be acknowledged.',
      );
    } finally {
      setAcknowledgingId(null);
    }
  };

  return (
    <>
      <section className="grid gap-2">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-2xl font-bold tracking-tight">Operational incidents</h2>
            <p className="text-sm text-muted-foreground">
              Debounced conditions and bounded resolved history from manager-owned evidence.
            </p>
          </div>
          <div className="text-right text-sm text-muted-foreground">
            {page ? `Updated ${formatTime(page.generatedAt)}` : 'Waiting for incidents'}
          </div>
        </div>
      </section>

      <section className="grid gap-3 rounded-lg border bg-card p-4 sm:grid-cols-4">
        <label className="grid gap-1 text-sm font-medium">
          Lifecycle
          <select
            className="h-9 rounded-md border bg-background px-3 text-sm"
            value={filter}
            onChange={(event) => setFilter(event.target.value as IncidentFilter)}
          >
            <option value="active">Active</option>
            <option value="resolved">Resolved</option>
            <option value="all">All history</option>
          </select>
        </label>
        <div className="grid content-center gap-1 text-sm">
          <span className="text-xs text-muted-foreground uppercase">Critical</span>
          <strong className="tabular-nums">{counts.critical}</strong>
        </div>
        <div className="grid content-center gap-1 text-sm">
          <span className="text-xs text-muted-foreground uppercase">Warning</span>
          <strong className="tabular-nums">{counts.warning}</strong>
        </div>
        <div className="flex items-end justify-between gap-3">
          <div className="grid gap-1 text-sm">
            <span className="text-xs text-muted-foreground uppercase">Acknowledged</span>
            <strong className="tabular-nums">{counts.acknowledged}</strong>
          </div>
          <Button type="button" size="sm" variant="outline" onClick={() => void load()}>
            Refresh
          </Button>
        </div>
      </section>

      <div
        className={
          error ? 'rounded-lg border border-red-300 bg-red-50 p-4 text-red-900' : 'sr-only'
        }
        role={error ? 'alert' : 'status'}
        aria-live="polite"
      >
        {error ?? (isLoading ? 'Loading operational incidents.' : '')}
      </div>

      {page?.truncated ? (
        <p className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
          Showing only the newest incidents allowed by the server response limit.
        </p>
      ) : null}

      {isLoading && !page ? <p className="text-muted-foreground">Loading incidents…</p> : null}

      {!isLoading && page?.incidents.length === 0 ? (
        <Card>
          <CardHeader>
            <CardTitle>No {filter === 'all' ? '' : `${filter} `}incidents</CardTitle>
            <CardDescription>
              Brief conditions remain hidden unless they cross their debounce boundary.
            </CardDescription>
          </CardHeader>
        </Card>
      ) : null}

      {page && page.incidents.length > 0 ? (
        <section className="overflow-x-auto rounded-lg border bg-card">
          <table className="w-full min-w-6xl text-left text-sm">
            <caption className="p-3 text-left text-sm font-semibold">
              {filter === 'active'
                ? 'Active operational incidents'
                : 'Operational incident history'}
            </caption>
            <thead className="bg-muted/50 text-xs text-muted-foreground uppercase">
              <tr>
                <th scope="col" className="px-4 py-3 font-medium">
                  State
                </th>
                <th scope="col" className="px-4 py-3 font-medium">
                  Incident
                </th>
                <th scope="col" className="px-4 py-3 font-medium">
                  Reason
                </th>
                <th scope="col" className="px-4 py-3 font-medium">
                  Timeline
                </th>
                <th scope="col" className="px-4 py-3 font-medium">
                  Action
                </th>
              </tr>
            </thead>
            <tbody>
              {page.incidents.map((incident) => (
                <IncidentRow
                  key={incident.incidentId}
                  incident={incident}
                  canAcknowledge={canAcknowledge}
                  isAcknowledging={acknowledgingId === incident.incidentId}
                  onAcknowledge={(selected) => void acknowledge(selected)}
                  node={nodes.find((node) => node.nodeId === incident.nodeId)}
                />
              ))}
            </tbody>
          </table>
        </section>
      ) : null}
    </>
  );
}
