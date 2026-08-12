import type { FleetNode } from '@/core/fleet';

import type { IncidentFilter, OperationalIncident } from './incidentsApi';

export type IncidentView = 'attention' | 'active' | 'resolved' | 'history';
export type SeverityFilter = 'all' | OperationalIncident['severity'];
export type IncidentSort = 'priority' | 'newest' | 'oldest' | 'observed';

const incidentViews = new Set<IncidentView>(['attention', 'active', 'resolved', 'history']);
const severityFilters = new Set<SeverityFilter>(['all', 'critical', 'warning']);
const incidentSorts = new Set<IncidentSort>(['priority', 'newest', 'oldest', 'observed']);

export const viewLabels: Record<IncidentView, string> = {
  attention: 'Needs attention',
  active: 'All active',
  resolved: 'Resolved',
  history: 'All history',
};

export const sortLabels: Record<IncidentSort, string> = {
  priority: 'Priority',
  newest: 'Newest triggered',
  oldest: 'Oldest triggered',
  observed: 'Recently observed',
};

export function parseIncidentView(value: string | null): IncidentView {
  return value != null && incidentViews.has(value as IncidentView)
    ? (value as IncidentView)
    : 'attention';
}

export function parseSeverityFilter(value: string | null): SeverityFilter {
  return value != null && severityFilters.has(value as SeverityFilter)
    ? (value as SeverityFilter)
    : 'all';
}

export function parseIncidentSort(value: string | null): IncidentSort {
  return value != null && incidentSorts.has(value as IncidentSort)
    ? (value as IncidentSort)
    : 'priority';
}

export function apiFilterForView(view: IncidentView): IncidentFilter {
  switch (view) {
    case 'attention':
    case 'active':
      return 'active';
    case 'resolved':
      return 'resolved';
    case 'history':
      return 'all';
  }
}

export function compareIncidents(
  left: OperationalIncident,
  right: OperationalIncident,
  sort: IncidentSort,
): number {
  if (sort === 'newest') return right.triggeredAt.localeCompare(left.triggeredAt);
  if (sort === 'oldest') return left.triggeredAt.localeCompare(right.triggeredAt);
  if (sort === 'observed') return right.lastObservedAt.localeCompare(left.lastObservedAt);

  const statusRank = { triggered: 0, acknowledged: 1, resolved: 2 } as const;
  const severityRank = { critical: 0, warning: 1 } as const;
  return (
    statusRank[left.status] - statusRank[right.status] ||
    severityRank[left.severity] - severityRank[right.severity] ||
    right.lastObservedAt.localeCompare(left.lastObservedAt) ||
    left.title.localeCompare(right.title)
  );
}

export function matchesIncidentSearch(
  incident: OperationalIncident,
  node: FleetNode | undefined,
  query: string,
): boolean {
  if (!query) return true;
  return [
    incident.title,
    incident.summary,
    incident.reason,
    incident.evidence,
    incident.kind,
    incident.profileId,
    node?.displayName,
  ].some((value) => value?.toLocaleLowerCase().includes(query));
}
