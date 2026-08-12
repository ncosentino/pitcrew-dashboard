import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { useSession } from '@/core/auth';
import { getFleet, type FleetNode } from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { EmptyState } from '@/core/ui/EmptyState';
import type { FilterChipDescriptor } from '@/core/ui/FilterChips';
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
import { IncidentFilters } from './components/IncidentFilters';
import { IncidentRow } from './components/IncidentRow';
import {
  apiFilterForView,
  compareIncidents,
  matchesIncidentSearch,
  parseIncidentSort,
  parseIncidentView,
  parseSeverityFilter,
  sortLabels,
  viewLabels,
} from './incidentView';

/** Renders active incidents and bounded resolved history without crowding fleet status pages. */
export default function IncidentsPage() {
  const { tenantId = '' } = useParams();
  const { session } = useSession();
  const [searchParams, setSearchParams] = useSearchParams();
  const [nodes, setNodes] = useState<ReadonlyArray<FleetNode>>([]);
  const [page, setPage] = useState<IncidentPage | null>(null);
  const [loadedFilter, setLoadedFilter] = useState<IncidentFilter | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [acknowledgingId, setAcknowledgingId] = useState<string | null>(null);
  const requestVersion = useRef(0);
  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);
  const canAcknowledge = tenant?.role === 'administrator' || tenant?.role === 'owner';
  const antiforgeryToken = session?.antiforgeryToken ?? '';
  const view = parseIncidentView(searchParams.get('view'));
  const severity = parseSeverityFilter(searchParams.get('severity'));
  const sort = parseIncidentSort(searchParams.get('sort'));
  const query = searchParams.get('q')?.trim().toLocaleLowerCase() ?? '';
  const sourceFilter = apiFilterForView(view);

  const setParameter = useCallback(
    (key: string, value: string, defaultValue: string) => {
      const next = new URLSearchParams(searchParams);
      if (!value || value === defaultValue) next.delete(key);
      else next.set(key, value);
      setSearchParams(next, { replace: true });
    },
    [searchParams, setSearchParams],
  );

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
        const next = await getIncidents(tenantId, sourceFilter, signal);
        if (version !== requestVersion.current) return;
        setPage(next);
        setLoadedFilter(sourceFilter);
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
    [sourceFilter, tenantId],
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

  const nodesById = useMemo(
    () => new Map(nodes.map((node) => [node.nodeId, node] as const)),
    [nodes],
  );
  const currentPage = loadedFilter === sourceFilter ? page : null;
  const counts = useMemo(() => {
    const incidents = currentPage?.incidents ?? [];
    return {
      total: incidents.length,
      acknowledged: incidents.filter((incident) => incident.status === 'acknowledged').length,
    };
  }, [currentPage]);
  const visibleIncidents = useMemo(() => {
    const incidents = currentPage?.incidents ?? [];
    return incidents
      .filter((incident) => view !== 'attention' || incident.status === 'triggered')
      .filter((incident) => severity === 'all' || incident.severity === severity)
      .filter((incident) => matchesIncidentSearch(incident, nodesById.get(incident.nodeId), query))
      .sort((left, right) => compareIncidents(left, right, sort));
  }, [currentPage, nodesById, query, severity, sort, view]);
  const visibleCritical = visibleIncidents.filter(
    (incident) => incident.severity === 'critical',
  ).length;
  const visibleWarning = visibleIncidents.length - visibleCritical;
  const filterChips = useMemo<ReadonlyArray<FilterChipDescriptor>>(() => {
    const chips: FilterChipDescriptor[] = [];
    if (view !== 'attention') {
      chips.push({
        key: 'view',
        label: 'View',
        value: viewLabels[view],
        onRemove: () => setParameter('view', 'attention', 'attention'),
      });
    }
    if (severity !== 'all') {
      chips.push({
        key: 'severity',
        label: 'Severity',
        value: severity,
        onRemove: () => setParameter('severity', 'all', 'all'),
      });
    }
    if (query) {
      chips.push({
        key: 'query',
        label: 'Search',
        value: searchParams.get('q')?.trim() ?? '',
        onRemove: () => setParameter('q', '', ''),
      });
    }
    if (sort !== 'priority') {
      chips.push({
        key: 'sort',
        label: 'Sort',
        value: sortLabels[sort],
        onRemove: () => setParameter('sort', 'priority', 'priority'),
      });
    }
    return chips;
  }, [query, searchParams, setParameter, severity, sort, view]);

  const resetView = useCallback(() => {
    setSearchParams({}, { replace: true });
  }, [setSearchParams]);

  const acknowledge = async (incident: OperationalIncident) => {
    setAcknowledgingId(incident.incidentId);
    setError(null);
    setNotice(null);
    try {
      await acknowledgeIncident(tenantId, incident.incidentId, antiforgeryToken);
      await load();
      setNotice(
        view === 'attention'
          ? `Acknowledged ${incident.title}. It remains active and is now hidden from Needs attention.`
          : `Acknowledged ${incident.title}. The incident remains active.`,
      );
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
            {currentPage
              ? `Updated ${formatTime(currentPage.generatedAt)}`
              : 'Waiting for incidents'}
          </div>
        </div>
      </section>

      {page ? (
        <IncidentFilters
          view={view}
          query={searchParams.get('q') ?? ''}
          severity={severity}
          sort={sort}
          chips={filterChips}
          resultSummary={
            !currentPage
              ? `Loading ${viewLabels[view].toLocaleLowerCase()} incidents…`
              : view === 'attention'
                ? `${visibleIncidents.length} need attention · ${visibleCritical} critical · ${visibleWarning} warning${counts.acknowledged > 0 ? ` · ${counts.acknowledged} acknowledged hidden` : ''}`
                : `${visibleIncidents.length} of ${counts.total} incidents shown`
          }
          onParameterChange={setParameter}
          onReset={resetView}
          onRefresh={() => void load()}
        />
      ) : null}

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

      {currentPage?.truncated ? (
        <StateBanner tone="caution" role="status">
          Showing only the newest incidents allowed by the server response limit.
        </StateBanner>
      ) : null}

      {isLoading && !currentPage ? <LoadingState label="Loading operational incidents…" /> : null}

      {!isLoading && currentPage?.incidents.length === 0 ? (
        <EmptyState
          title={
            view === 'attention' || view === 'active'
              ? 'No active incidents'
              : view === 'resolved'
                ? 'No resolved incidents'
                : 'No incident history'
          }
          description="Brief conditions remain hidden unless they cross their debounce boundary. This does not prove the fleet is healthy — only that no qualifying condition is visible."
        />
      ) : null}

      {currentPage && currentPage.incidents.length > 0 && visibleIncidents.length === 0 ? (
        <EmptyState
          title={
            view === 'attention' && counts.acknowledged > 0
              ? 'No incidents need attention'
              : 'No incidents match this view'
          }
          description={
            view === 'attention' && counts.acknowledged > 0
              ? `${counts.acknowledged} active ${counts.acknowledged === 1 ? 'incident is' : 'incidents are'} acknowledged and hidden from this queue. Switch to All active to review them.`
              : 'Change or reset the filters to return to the active attention queue.'
          }
          action={
            <Button type="button" variant="outline" onClick={resetView}>
              Reset incident view
            </Button>
          }
        />
      ) : null}

      {currentPage && visibleIncidents.length > 0 ? (
        <>
          <div className="grid gap-3 lg:hidden" data-testid="incidents-mobile-summary">
            {visibleIncidents.map((incident) => (
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
            label={`${viewLabels[view]} operational incidents`}
          >
            <table className="w-full min-w-5xl text-left text-sm">
              <caption className="p-3 text-left text-sm font-semibold">
                {viewLabels[view]} operational incidents
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
                {visibleIncidents.map((incident) => (
                  <IncidentRow
                    key={incident.incidentId}
                    incident={incident}
                    canAcknowledge={canAcknowledge}
                    isAcknowledging={acknowledgingId === incident.incidentId}
                    onAcknowledge={(selected) => void acknowledge(selected)}
                    onUnacknowledge={(selected) => void unacknowledge(selected)}
                    node={nodesById.get(incident.nodeId)}
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
