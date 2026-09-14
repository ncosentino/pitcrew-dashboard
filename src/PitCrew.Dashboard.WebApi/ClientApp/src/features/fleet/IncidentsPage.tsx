import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { useSession } from '@/core/auth';
import { getFleet, type FleetNode } from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { EmptyState } from '@/core/ui/EmptyState';
import type { FilterChipDescriptor } from '@/core/ui/FilterChips';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalList } from '@/core/ui/OperationalList';
import { ReadinessSummary } from '@/core/ui/ReadinessSummary';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import {
  acknowledgeIncident,
  getIncidentOrNull,
  getIncidents,
  unacknowledgeIncident,
  type IncidentFilter,
  type IncidentPage,
  type OperationalIncident,
} from './incidentsApi';
import { IncidentDetail, type IncidentEnrichmentStatus } from './components/IncidentDetail';
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

const desktopIncidentWorkspaceQuery = '(min-width: 80rem)';
const expandedIncidentFilterQuery = '(min-width: 48rem)';

function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(() => globalThis.matchMedia(query).matches);

  useEffect(() => {
    const mediaQuery = globalThis.matchMedia(query);
    const handleChange = (event: MediaQueryListEvent) => setMatches(event.matches);
    mediaQuery.addEventListener('change', handleChange);
    return () => mediaQuery.removeEventListener('change', handleChange);
  }, [query]);

  return matches;
}

interface IncidentEnrichmentState {
  readonly tenantId: string;
  readonly nodes: ReadonlyArray<FleetNode>;
  readonly status: IncidentEnrichmentStatus;
}

/** Renders active incidents and bounded resolved history without crowding fleet status pages. */
export default function IncidentsPage() {
  const { tenantId = '' } = useParams();
  const { session } = useSession();
  const [searchParams, setSearchParams] = useSearchParams();
  const [enrichment, setEnrichment] = useState<IncidentEnrichmentState>({
    tenantId,
    nodes: [],
    status: 'loading',
  });
  const [page, setPage] = useState<IncidentPage | null>(null);
  const [exactIncident, setExactIncident] = useState<OperationalIncident | null>(null);
  const [loadedFilter, setLoadedFilter] = useState<IncidentFilter | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [acknowledgingId, setAcknowledgingId] = useState<string | null>(null);
  const [isQueueOpenOnMobile, setIsQueueOpenOnMobile] = useState(false);
  const requestVersion = useRef(0);
  const continuationRequest = useRef<{
    controller: AbortController;
    query: string;
  } | null>(null);
  const mutationController = useRef<AbortController | null>(null);
  const mutationVersion = useRef(0);
  const selectedCase = useRef<HTMLDivElement>(null);
  const pendingSelectionFocus = useRef<string | null>(null);
  const isDesktopWorkspace = useMediaQuery(desktopIncidentWorkspaceQuery);
  const isExpandedFilterLayout = useMediaQuery(expandedIncidentFilterQuery);
  const currentEnrichment =
    enrichment.tenantId === tenantId
      ? enrichment
      : { tenantId, nodes: [], status: 'loading' as const };
  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);
  const canAcknowledge = tenant?.role === 'administrator' || tenant?.role === 'owner';
  const antiforgeryToken = session?.antiforgeryToken ?? '';
  const view = parseIncidentView(searchParams.get('view'));
  const severity = parseSeverityFilter(searchParams.get('severity'));
  const sort = parseIncidentSort(searchParams.get('sort'));
  const query = searchParams.get('q')?.trim().toLocaleLowerCase() ?? '';
  const sourceFilter = apiFilterForView(view);
  const requestedIncidentId = searchParams.get('incident') || null;
  const continuationQuery = `${tenantId}|${sourceFilter}|${view}|${severity}|${query}|${sort}`;

  const setParameter = useCallback(
    (key: string, value: string, defaultValue: string) => {
      const next = new URLSearchParams(searchParams);
      if (!value || value === defaultValue) next.delete(key);
      else next.set(key, value);
      if (key === 'view') next.delete('incident');
      setSearchParams(next, { replace: true });
    },
    [searchParams, setSearchParams],
  );

  const load = useCallback(
    async (signal?: AbortSignal) => {
      continuationRequest.current?.controller.abort();
      const version = ++requestVersion.current;
      setIsLoading(true);
      try {
        const requestSignal = signal ?? new AbortController().signal;
        const fleetPromise = getFleet(tenantId, requestSignal)
          .then((nextFleet) => ({ status: 'available' as const, nodes: nextFleet.nodes }))
          .catch((caught: unknown) => {
            if (caught instanceof DOMException && caught.name === 'AbortError') return null;
            console.warn('Connector health evidence is unavailable on the incident page.', caught);
            return { status: 'unavailable' as const, nodes: [] };
          });
        const next = await getIncidents(tenantId, sourceFilter, signal);
        if (version !== requestVersion.current) return;
        const selected =
          requestedIncidentId != null &&
          !next.incidents.some((incident) => incident.incidentId === requestedIncidentId)
            ? await getIncidentOrNull(tenantId, requestedIncidentId, signal)
            : null;
        if (version !== requestVersion.current) return;
        setPage(next);
        setExactIncident(selected);
        setLoadedFilter(sourceFilter);
        setError(null);
        void fleetPromise.then((result) => {
          if (version !== requestVersion.current || result == null) return;
          if (result.status === 'available') {
            setEnrichment({ tenantId, nodes: result.nodes, status: 'available' });
            return;
          }
          setEnrichment((current) =>
            current.tenantId === tenantId &&
            (current.status === 'available' || current.status === 'stale')
              ? { ...current, status: 'stale' }
              : { tenantId, nodes: [], status: 'unavailable' },
          );
        });
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === 'AbortError') return;
        if (version !== requestVersion.current) return;
        setError(caught instanceof Error ? caught.message : 'Incident history is unavailable.');
      } finally {
        if (!signal?.aborted && version === requestVersion.current) setIsLoading(false);
      }
    },
    [requestedIncidentId, sourceFilter, tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    const initial = globalThis.setTimeout(() => {
      void load(controller.signal);
    }, 0);
    const refresh = globalThis.setInterval(() => void load(controller.signal), 30_000);
    return () => {
      controller.abort();
      continuationRequest.current?.controller.abort();
      mutationController.current?.abort();
      mutationVersion.current += 1;
      globalThis.clearTimeout(initial);
      globalThis.clearInterval(refresh);
    };
  }, [load]);

  useEffect(() => {
    const request = continuationRequest.current;
    if (request == null) return;
    continuationRequest.current = null;
    request.controller.abort();
    requestVersion.current += 1;
    setIsLoading(false);
  }, [continuationQuery]);

  const nodesById = useMemo(
    () => new Map(currentEnrichment.nodes.map((node) => [node.nodeId, node] as const)),
    [currentEnrichment.nodes],
  );
  const currentPage = loadedFilter === sourceFilter ? page : null;
  const counts = useMemo(() => {
    const incidents = currentPage?.incidents ?? [];
    return {
      loaded: incidents.length,
      total: currentPage?.totalCount,
      acknowledged: incidents.filter((incident) => incident.operatorState === 'acknowledged')
        .length,
    };
  }, [currentPage]);
  const visibleIncidents = useMemo(() => {
    const incidents = currentPage?.incidents ?? [];
    return incidents
      .filter((incident) => view !== 'attention' || incident.operatorState === 'unowned')
      .filter((incident) => severity === 'all' || incident.currentSeverity === severity)
      .filter((incident) => matchesIncidentSearch(incident, nodesById.get(incident.nodeId), query))
      .sort((left, right) => compareIncidents(left, right, sort));
  }, [currentPage, nodesById, query, severity, sort, view]);
  const visibleCritical = visibleIncidents.filter(
    (incident) => incident.currentSeverity === 'critical',
  ).length;
  const visibleWarning = visibleIncidents.filter(
    (incident) => incident.currentSeverity === 'warning',
  ).length;
  const visibleTriggered = visibleIncidents.filter(
    (incident) => incident.operatorState === 'unowned',
  );
  const sourceHasTriggeredIncident =
    currentPage?.incidents.some((incident) => incident.operatorState === 'unowned') ?? false;
  const requestedIncident =
    requestedIncidentId == null
      ? undefined
      : (currentPage?.incidents.find((incident) => incident.incidentId === requestedIncidentId) ??
        exactIncident);
  const requestedIncidentIsUnavailable =
    currentPage != null && requestedIncidentId != null && requestedIncident == null;
  const selectedIncident =
    requestedIncidentId == null ? (visibleIncidents[0] ?? null) : (requestedIncident ?? null);
  const selectedNode = selectedIncident ? nodesById.get(selectedIncident.nodeId) : undefined;
  const selectedIncidentIsVisible =
    selectedIncident != null &&
    visibleIncidents.some((incident) => incident.incidentId === selectedIncident.incidentId);

  const selectIncident = useCallback(
    (incidentId: string) => {
      if (isDesktopWorkspace) return;
      pendingSelectionFocus.current = incidentId;
      setIsQueueOpenOnMobile(false);
    },
    [isDesktopWorkspace],
  );

  useEffect(() => {
    if (
      isDesktopWorkspace ||
      isQueueOpenOnMobile ||
      selectedIncident == null ||
      pendingSelectionFocus.current !== selectedIncident.incidentId
    ) {
      return;
    }
    pendingSelectionFocus.current = null;
    selectedCase.current?.focus();
  }, [isDesktopWorkspace, isQueueOpenOnMobile, selectedIncident]);

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
    mutationController.current?.abort();
    const controller = new AbortController();
    mutationController.current = controller;
    const version = ++mutationVersion.current;
    setAcknowledgingId(incident.incidentId);
    setError(null);
    setNotice(null);
    try {
      const updated = await acknowledgeIncident(
        tenantId,
        incident.incidentId,
        antiforgeryToken,
        controller.signal,
      );
      if (version !== mutationVersion.current || controller.signal.aborted) return;
      updateIncident(updated);
      setNotice(
        view === 'attention'
          ? `Acknowledged ${incident.title}. It remains active and is now hidden from Needs attention.`
          : `Acknowledged ${incident.title}. The incident remains active.`,
      );
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') return;
      if (version !== mutationVersion.current) return;
      setError(
        caught instanceof Error ? caught.message : 'The incident could not be acknowledged.',
      );
    } finally {
      if (mutationController.current === controller) mutationController.current = null;
      if (version === mutationVersion.current) setAcknowledgingId(null);
    }
  };

  const unacknowledge = async (incident: OperationalIncident) => {
    mutationController.current?.abort();
    const controller = new AbortController();
    mutationController.current = controller;
    const version = ++mutationVersion.current;
    setAcknowledgingId(incident.incidentId);
    setError(null);
    setNotice(null);
    try {
      const updated = await unacknowledgeIncident(
        tenantId,
        incident.incidentId,
        antiforgeryToken,
        controller.signal,
      );
      if (version !== mutationVersion.current || controller.signal.aborted) return;
      updateIncident(updated);
      setNotice(`Unacknowledged ${incident.title}. The incident returned to triggered.`);
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') return;
      if (version !== mutationVersion.current) return;
      setError(
        caught instanceof Error ? caught.message : 'The incident could not be unacknowledged.',
      );
    } finally {
      if (mutationController.current === controller) mutationController.current = null;
      if (version === mutationVersion.current) setAcknowledgingId(null);
    }
  };

  const updateIncident = (updated: OperationalIncident) => {
    setPage((current) =>
      current == null
        ? current
        : {
            ...current,
            incidents: current.incidents.map((incident) =>
              incident.incidentId === updated.incidentId ? updated : incident,
            ),
          },
    );
    setExactIncident((current) => (current?.incidentId === updated.incidentId ? updated : current));
  };

  const loadNextPage = async () => {
    const cursor = currentPage?.nextCursor;
    if (!cursor) return;
    continuationRequest.current?.controller.abort();
    const controller = new AbortController();
    const requestedQuery = continuationQuery;
    const request = { controller, query: requestedQuery };
    continuationRequest.current = request;
    const version = ++requestVersion.current;
    const requestedTenantId = tenantId;
    const requestedFilter = sourceFilter;
    setIsLoading(true);
    setError(null);
    try {
      const next = await getIncidents(
        requestedTenantId,
        requestedFilter,
        controller.signal,
        cursor,
      );
      if (
        version !== requestVersion.current ||
        controller.signal.aborted ||
        continuationRequest.current?.query !== requestedQuery
      ) {
        return;
      }
      setPage((current) => {
        if (current == null || current.nextCursor !== cursor || loadedFilter !== requestedFilter) {
          return current;
        }
        const knownIds = new Set(current.incidents.map((incident) => incident.incidentId));
        return {
          ...next,
          incidents: [
            ...current.incidents,
            ...next.incidents.filter((incident) => !knownIds.has(incident.incidentId)),
          ],
        };
      });
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') return;
      if (version !== requestVersion.current) return;
      setError(caught instanceof Error ? caught.message : 'More incidents could not be loaded.');
    } finally {
      if (continuationRequest.current === request) continuationRequest.current = null;
      if (version === requestVersion.current && !controller.signal.aborted) setIsLoading(false);
    }
  };

  return (
    <>
      <ReadinessSummary
        title="Incident work queue"
        description="Debounced operational exceptions and bounded history from manager-owned evidence. Acknowledgement records ownership; only new evidence resolves a condition."
        narrowColumns={2}
        status={
          <StatusBadge
            status={
              !currentPage
                ? error
                  ? 'Status unavailable'
                  : 'Loading'
                : view === 'resolved' || view === 'history'
                  ? 'Historical view'
                  : (currentPage.criticalCount ?? 0) > 0
                    ? 'Critical attention'
                    : visibleTriggered.length > 0
                      ? 'Needs attention'
                      : sourceHasTriggeredIncident
                        ? 'No matching attention'
                        : visibleIncidents.length > 0
                          ? 'Active incidents owned'
                          : 'No incident needs attention'
            }
            tone={
              !currentPage
                ? error
                  ? 'critical'
                  : 'neutral'
                : view === 'resolved' || view === 'history'
                  ? 'neutral'
                  : (currentPage.criticalCount ?? 0) > 0
                    ? 'critical'
                    : visibleTriggered.length > 0 || visibleIncidents.length > 0
                      ? 'caution'
                      : sourceHasTriggeredIncident
                        ? 'neutral'
                        : 'positive'
            }
          />
        }
        items={[
          {
            label: 'Observation',
            value: currentPage
              ? formatTime(currentPage.generatedAt)
              : error
                ? 'Unavailable'
                : 'Loading…',
            detail: 'Response generated; source and receipt clocks remain separate',
          },
          {
            label: 'Queue results',
            value: currentPage
              ? (counts.total ?? 'Unavailable')
              : error
                ? 'Unavailable'
                : 'Loading…',
            detail: `${counts.loaded} loaded · ${viewLabels[view]}`,
          },
          {
            label: 'Critical in view',
            value: currentPage
              ? (currentPage.criticalCount ?? 'Unavailable')
              : error
                ? 'Unavailable'
                : 'Loading…',
            detail:
              currentPage?.warningCount == null
                ? 'Authoritative severity totals unavailable'
                : `${currentPage.warningCount} warning across all pages`,
          },
          {
            label: 'Acknowledged',
            value: currentPage ? counts.acknowledged : error ? 'Unavailable' : 'Loading…',
            detail: 'Still active until evidence resolves them',
          },
        ]}
      />

      {page ? (
        <IncidentFilters
          view={view}
          query={searchParams.get('q') ?? ''}
          severity={severity}
          sort={sort}
          isExpandedLayout={isExpandedFilterLayout}
          chips={filterChips}
          resultSummary={
            !currentPage
              ? `Loading ${viewLabels[view].toLocaleLowerCase()} incidents…`
              : view === 'attention'
                ? `${visibleIncidents.length} need attention · ${visibleCritical} critical · ${visibleWarning} warning${counts.acknowledged > 0 ? ` · ${counts.acknowledged} acknowledged hidden` : ''}`
                : counts.total == null
                  ? `${visibleIncidents.length} filtered · ${counts.loaded} loaded`
                  : `${visibleIncidents.length} filtered · ${counts.loaded} of ${counts.total} loaded`
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
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>
              {counts.total == null
                ? `Showing ${counts.loaded} server-bounded incidents; authoritative total unavailable.`
                : `Showing ${counts.loaded} of ${counts.total} authoritative matching incidents.`}
            </span>
            {currentPage.nextCursor ? (
              <Button
                type="button"
                size="sm"
                variant="outline"
                disabled={isLoading}
                onClick={() => void loadNextPage()}
              >
                {isLoading ? 'Loading…' : 'Load more incidents'}
              </Button>
            ) : null}
          </div>
        </StateBanner>
      ) : null}

      {isLoading && !currentPage ? <LoadingState label="Loading operational incidents…" /> : null}

      {!isLoading && currentPage?.incidents.length === 0 && !requestedIncidentIsUnavailable ? (
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

      {currentPage &&
      currentPage.incidents.length > 0 &&
      visibleIncidents.length === 0 &&
      selectedIncident == null &&
      !requestedIncidentIsUnavailable ? (
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

      {requestedIncidentIsUnavailable && requestedIncidentId ? (
        <StateBanner tone="caution" role="status">
          <div className="grid gap-3">
            <div>
              <p className="font-semibold">Selected incident is unavailable</p>
              <p className="mt-1">
                Incident {requestedIncidentId} is not present in this bounded{' '}
                {viewLabels[view].toLocaleLowerCase()} response. It may have resolved, fallen
                outside retention, or been omitted by the server limit; another incident has not
                been substituted.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => setParameter('incident', '', '')}
              >
                Clear selection
              </Button>
              <Button asChild type="button" size="sm" variant="outline">
                <Link to={incidentHistoryHref(searchParams, requestedIncidentId)}>
                  Search all history
                </Link>
              </Button>
            </div>
          </div>
        </StateBanner>
      ) : null}

      {currentPage && (visibleIncidents.length > 0 || selectedIncident != null) ? (
        <div
          className={
            visibleIncidents.length > 0 && selectedIncident != null
              ? 'grid min-w-0 gap-4 xl:grid-cols-[minmax(19rem,0.78fr)_minmax(0,1.4fr)] xl:items-start'
              : 'grid min-w-0 gap-4'
          }
        >
          {visibleIncidents.length > 0 ? (
            <details
              className="group min-w-0 rounded-xl border bg-card xl:contents"
              open={isDesktopWorkspace || isQueueOpenOnMobile}
              onToggle={(event) => {
                if (!isDesktopWorkspace) setIsQueueOpenOnMobile(event.currentTarget.open);
              }}
            >
              <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 text-sm font-semibold outline-none transition-colors hover:bg-muted/40 focus-visible:ring-2 focus-visible:ring-ring xl:hidden">
                <span>Choose incident</span>
                <span className="text-xs font-normal text-muted-foreground">
                  {visibleIncidents.length} in queue
                </span>
              </summary>
              <section className="min-w-0 border-t xl:border-0">
                <div className="flex flex-wrap items-end justify-between gap-2 px-4 py-3 xl:mb-2 xl:px-0 xl:py-0">
                  <div>
                    <h2 className="text-base font-semibold">Incident queue</h2>
                    <p className="text-sm text-muted-foreground">
                      Attention-ordered records matching the current view.
                    </p>
                  </div>
                  <span className="text-xs text-muted-foreground">
                    {visibleIncidents.length} shown
                  </span>
                </div>
                <IncidentQueue
                  incidents={visibleIncidents}
                  nodesById={nodesById}
                  enrichmentStatus={currentEnrichment.status}
                  searchParams={searchParams}
                  selectedIncidentId={selectedIncident?.incidentId ?? null}
                  onSelect={selectIncident}
                  className="rounded-none border-0 xl:rounded-xl xl:border"
                />
              </section>
            </details>
          ) : null}

          {selectedIncident ? (
            <div
              ref={selectedCase}
              aria-label="Selected incident case"
              className="min-w-0 rounded-xl outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
              role="region"
              tabIndex={-1}
            >
              <IncidentDetail
                incident={selectedIncident}
                node={selectedNode}
                tenantId={tenantId}
                enrichmentStatus={currentEnrichment.status}
                isVisible={selectedIncidentIsVisible}
                canAcknowledge={canAcknowledge}
                canRequestSupportDiagnostics={canAcknowledge}
                isAcknowledging={acknowledgingId === selectedIncident.incidentId}
                onAcknowledge={() => void acknowledge(selectedIncident)}
                onUnacknowledge={() => void unacknowledge(selectedIncident)}
              />
            </div>
          ) : null}
        </div>
      ) : null}
    </>
  );
}

interface IncidentQueueProps {
  readonly incidents: ReadonlyArray<OperationalIncident>;
  readonly nodesById: ReadonlyMap<string, FleetNode>;
  readonly enrichmentStatus: IncidentEnrichmentStatus;
  readonly searchParams: URLSearchParams;
  readonly selectedIncidentId: string | null;
  readonly onSelect: (incidentId: string) => void;
  readonly className?: string;
}

function IncidentQueue({
  incidents,
  nodesById,
  enrichmentStatus,
  searchParams,
  selectedIncidentId,
  onSelect,
  className,
}: IncidentQueueProps) {
  return (
    <OperationalList label="Operational incident queue" className={className}>
      {incidents.map((incident) => {
        const node = nodesById.get(incident.nodeId);
        const nextSearchParams = new URLSearchParams(searchParams);
        nextSearchParams.set('incident', incident.incidentId);
        return (
          <IncidentRow
            key={incident.incidentId}
            incident={incident}
            node={node}
            enrichmentStatus={enrichmentStatus}
            selectionHref={`?${nextSearchParams.toString()}`}
            selected={incident.incidentId === selectedIncidentId}
            onSelect={onSelect}
          />
        );
      })}
    </OperationalList>
  );
}

function incidentHistoryHref(searchParams: URLSearchParams, incidentId: string): string {
  const next = new URLSearchParams(searchParams);
  next.set('view', 'history');
  next.set('incident', incidentId);
  return `?${next.toString()}`;
}
