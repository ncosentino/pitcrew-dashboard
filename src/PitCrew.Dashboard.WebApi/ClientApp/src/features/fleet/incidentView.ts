import type { FleetNode } from '@/core/fleet';

import type { IncidentFilter, OperationalIncident } from './incidentsApi';

export type IncidentView = 'attention' | 'active' | 'resolved' | 'history';
export type SeverityFilter = 'all' | OperationalIncident['severity'];
export type IncidentSort = 'priority' | 'newest' | 'oldest' | 'observed';

const incidentViews = new Set<IncidentView>(['attention', 'active', 'resolved', 'history']);
const severityFilters = new Set<SeverityFilter>(['all', 'critical', 'warning']);
const incidentSorts = new Set<IncidentSort>(['priority', 'newest', 'oldest', 'observed']);

export const viewLabels: Record<IncidentView, string> = {
  attention: 'Needs action',
  active: 'All open',
  resolved: 'Resolved history',
  history: 'All records',
};

export const sortLabels: Record<IncidentSort, string> = {
  priority: 'Priority',
  newest: 'Newest triggered',
  oldest: 'Oldest triggered',
  observed: 'Recently observed',
};

export function incidentConditionLabel(
  conditionState: OperationalIncident['conditionState'],
): string {
  switch (conditionState) {
    case 'confirmed':
      return 'Confirmed problem';
    case 'waiting-for-evidence':
      return 'Waiting for evidence';
    case 'recovering':
      return 'Recovery observed';
    case 'monitoring-ended':
      return 'Monitoring ended';
    case 'legacy-unverified':
      return 'Legacy evidence';
    case 'resolved':
      return 'Resolved history';
  }
}

export function isActionableIncident(incident: OperationalIncident): boolean {
  return incident.conditionState === 'confirmed' && incident.operatorState === 'unowned';
}

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

  const conditionRank = {
    confirmed: 0,
    recovering: 1,
    'waiting-for-evidence': 2,
    'monitoring-ended': 3,
    'legacy-unverified': 4,
    resolved: 5,
  } as const;
  const operatorRank = { unowned: 0, acknowledged: 1 } as const;
  const severityRank = { critical: 0, warning: 1 } as const;
  return (
    conditionRank[left.conditionState] - conditionRank[right.conditionState] ||
    operatorRank[left.operatorState] - operatorRank[right.operatorState] ||
    (left.currentSeverity == null ? 2 : severityRank[left.currentSeverity]) -
      (right.currentSeverity == null ? 2 : severityRank[right.currentSeverity]) ||
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
