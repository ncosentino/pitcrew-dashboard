import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { useSession } from '@/core/auth';
import { getFleet, type FleetNode } from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { EmptyState } from '@/core/ui/EmptyState';
import { FilterToolbar } from '@/core/ui/FilterToolbar';
import { FormField } from '@/core/ui/FormField';
import { LoadingState } from '@/core/ui/LoadingState';
import { ScrollableRegion } from '@/core/ui/ScrollableRegion';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { typography } from '@/core/ui/typography';

import {
  acknowledgeIncident,
  getIncidents,
  unacknowledgeIncident,
  type IncidentFilter,
  type IncidentPage,
  type OperationalIncident,
} from './incidentsApi';

interface IncidentRowProps {
  readonly incident: OperationalIncident;
  readonly canAcknowledge: boolean;
  readonly isAcknowledging: boolean;
  readonly onAcknowledge: (incident: OperationalIncident) => void;
  readonly onUnacknowledge: (incident: OperationalIncident) => void;
  readonly node: FleetNode | undefined;
}

function IncidentRow({
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

/** Renders active incidents and bounded resolved history without crowding fleet status pages. */
export default function IncidentsPage() {
  const { tenantId = '' } = useParams();
  const { session } = useSession();
  const [nodes, setNodes] = useState<ReadonlyArray<FleetNode>>([]);
  const [filter, setFilter] = useState<IncidentFilter>('active');
  const [page, setPage] = useState<IncidentPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
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
    setNotice(null);
    try {
      await acknowledgeIncident(tenantId, incident.incidentId, antiforgeryToken);
      await load();
      setNotice(`Acknowledged ${incident.title}. The incident remains active.`);
    } catch (caught) {
      setError(
        caught instanceof Error ? caught.message : 'The incident could not be acknowledged.',
      );
    } finally {
      setAcknowledgingId(null);
    }
  };

  const unacknowledge = async (incident: OperationalIncident) => {
    setAcknowledgingId(incident.incidentId);
    setError(null);
    setNotice(null);
    try {
      await unacknowledgeIncident(tenantId, incident.incidentId, antiforgeryToken);
      await load();
      setNotice(`Unacknowledged ${incident.title}. The incident returned to triggered.`);
    } catch (caught) {
      setError(
        caught instanceof Error ? caught.message : 'The incident could not be unacknowledged.',
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
            <h2 className={typography.sectionHeading}>Operational incidents</h2>
            <p className="text-sm text-muted-foreground">
              Debounced conditions and bounded resolved history from manager-owned evidence.
            </p>
          </div>
          <div className="text-right text-sm text-muted-foreground">
            {page ? `Updated ${formatTime(page.generatedAt)}` : 'Waiting for incidents'}
          </div>
        </div>
      </section>

      <FilterToolbar label="Incident filters and summary">
        <FormField label="Lifecycle">
          <select
            className="h-9 rounded-md border bg-background px-3 text-sm"
            value={filter}
            onChange={(event) => setFilter(event.target.value as IncidentFilter)}
          >
            <option value="active">Active</option>
            <option value="resolved">Resolved</option>
            <option value="all">All history</option>
          </select>
        </FormField>
        <div className="grid content-center gap-1 text-sm">
          <span className="text-xs font-semibold text-muted-foreground uppercase">Critical</span>
          <strong className="tabular-nums">{counts.critical}</strong>
        </div>
        <div className="grid content-center gap-1 text-sm">
          <span className="text-xs font-semibold text-muted-foreground uppercase">Warning</span>
          <strong className="tabular-nums">{counts.warning}</strong>
        </div>
        <div className="flex items-end justify-between gap-3">
          <div className="grid gap-1 text-sm">
            <span className="text-xs font-semibold text-muted-foreground uppercase">
              Acknowledged
            </span>
            <strong className="tabular-nums">{counts.acknowledged}</strong>
          </div>
          <Button type="button" size="sm" variant="outline" onClick={() => void load()}>
            Refresh
          </Button>
        </div>
      </FilterToolbar>

      {error ? (
        <StateBanner tone="critical" role="alert">
          {error}
        </StateBanner>
      ) : notice ? (
        <StateBanner tone="positive" role="status">
          {notice}
        </StateBanner>
      ) : isLoading ? (
        <div className="sr-only" role="status" aria-live="polite">
          Loading operational incidents.
        </div>
      ) : null}

      {page?.truncated ? (
        <StateBanner tone="caution" role="status">
          Showing only the newest incidents allowed by the server response limit.
        </StateBanner>
      ) : null}

      {isLoading && !page ? <LoadingState label="Loading operational incidents…" /> : null}

      {!isLoading && page?.incidents.length === 0 ? (
        <EmptyState
          title={`No ${filter === 'all' ? '' : `${filter} `}incidents`}
          description="Brief conditions remain hidden unless they cross their debounce boundary. This does not prove the fleet is healthy — only that no qualifying condition is visible."
        />
      ) : null}

      {page && page.incidents.length > 0 ? (
        <>
          <div className="grid gap-3 lg:hidden" data-testid="incidents-mobile-summary">
            {page.incidents.map((incident) => (
              <div
                key={incident.incidentId}
                className="grid gap-2 rounded-lg border bg-card p-4"
                data-testid={`incident-card-${incident.incidentId}`}
              >
                <div className="flex flex-wrap items-center gap-2">
                  <StatusBadge status={incident.severity} />
                  <StatusBadge status={incident.status} />
                </div>
                <Link
                  className="min-w-0 break-words font-semibold text-link underline-offset-4 hover:underline"
                  to={incident.link}
                >
                  {incident.title}
                </Link>
                <p className="text-xs text-muted-foreground">{incident.summary}</p>
                <div className="text-xs text-muted-foreground">
                  Triggered {formatTime(incident.triggeredAt)}
                </div>
                {canAcknowledge && incident.status === 'triggered' ? (
                  <div className="grid gap-1">
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      className="min-h-11 justify-self-start"
                      disabled={acknowledgingId === incident.incidentId}
                      onClick={() => void acknowledge(incident)}
                    >
                      {acknowledgingId === incident.incidentId ? 'Acknowledging…' : 'Acknowledge'}
                    </Button>
                    <span className="text-xs text-muted-foreground">
                      Records operator ownership without resolving the condition. Reversible while
                      active.
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
                      className="min-h-11 justify-self-start"
                      disabled={acknowledgingId === incident.incidentId}
                      onClick={() => void unacknowledge(incident)}
                    >
                      {acknowledgingId === incident.incidentId ? 'Reverting…' : 'Unacknowledge'}
                    </Button>
                    <span className="text-xs text-muted-foreground">
                      Returns this active incident to triggered.
                    </span>
                  </div>
                ) : null}
              </div>
            ))}
          </div>

          <ScrollableRegion
            className="hidden rounded-lg border bg-card lg:block"
            label={
              filter === 'active' ? 'Active operational incidents' : 'Operational incident history'
            }
          >
            <table className="w-full min-w-5xl text-left text-sm">
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
                    onUnacknowledge={(selected) => void unacknowledge(selected)}
                    node={nodes.find((node) => node.nodeId === incident.nodeId)}
                  />
                ))}
              </tbody>
            </table>
          </ScrollableRegion>
        </>
      ) : null}
    </>
  );
}
