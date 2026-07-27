import { useEffect, useState } from 'react';

import { getNodeHistory, getProfileHistory, type NodeHistoryResponse } from './historyApi';

/** Bounded history request issued by one node or profile history view. */
export interface FleetHistoryRequest {
  readonly tenantId: string;
  readonly nodeId: string;
  readonly profileId: string | null;
  readonly rangeHours: number;
  readonly resolution: 'raw' | 'hourly';
  readonly pointLimit: number;
  readonly eventLimit: number;
  readonly enabled: boolean;
}

/** Load state of one bounded history request. */
export interface FleetHistoryState {
  readonly history: NodeHistoryResponse | null;
  readonly error: string | null;
  readonly isLoading: boolean;
  readonly isStale: boolean;
}

interface HistoryResult {
  readonly key: string;
  readonly scope: string;
  readonly history: NodeHistoryResponse | null;
  readonly error: string | null;
}

const idle: HistoryResult = { key: '', scope: '', history: null, error: null };
const hourMilliseconds = 60 * 60 * 1000;

function floorHour(value: number): number {
  return value - (value % hourMilliseconds);
}

function ceilingHour(value: number): number {
  const floored = floorHour(value);
  return floored === value ? value : floored + hourMilliseconds;
}

/**
 * Loads bounded retained history for one node or profile.
 *
 * The range is always bounded by the caller and re-requested whenever the range or resolution
 * changes, so no unbounded query is ever issued. Hourly ranges are aligned to whole UTC hours so
 * the request matches the buckets the server can answer truthfully. The settled result is keyed by
 * the request it answered, so a stale response never replaces the current range, while the
 * previously loaded range for the same node stays visible during a refresh instead of blanking.
 */
export function useFleetHistory(request: FleetHistoryRequest): FleetHistoryState {
  const { tenantId, nodeId, profileId, rangeHours, resolution, pointLimit, eventLimit, enabled } =
    request;
  const isActive = enabled && tenantId !== '' && nodeId !== '';
  const scope = `${tenantId}|${nodeId}|${profileId ?? ''}`;
  const requestKey = isActive
    ? `${scope}|${rangeHours}|${resolution}|${pointLimit}|${eventLimit}`
    : '';
  const [result, setResult] = useState<HistoryResult>(idle);

  useEffect(() => {
    if (!isActive) return undefined;
    const controller = new AbortController();
    const now = Date.now();
    const to = resolution === 'hourly' ? floorHour(now) : now;
    const from =
      resolution === 'hourly'
        ? ceilingHour(now - rangeHours * hourMilliseconds)
        : now - rangeHours * hourMilliseconds;
    const query = {
      from: new Date(from).toISOString(),
      to: new Date(to).toISOString(),
      resolution,
      points: pointLimit,
      events: eventLimit,
    };
    const load = async () => {
      try {
        const history =
          profileId == null
            ? await getNodeHistory(tenantId, nodeId, query, controller.signal)
            : await getProfileHistory(tenantId, nodeId, profileId, query, controller.signal);
        if (!controller.signal.aborted) {
          setResult({ key: requestKey, scope, history, error: null });
        }
      } catch (caught) {
        if (controller.signal.aborted) return;
        setResult({
          key: requestKey,
          scope,
          history: null,
          error:
            caught instanceof Error ? caught.message : 'Historical telemetry could not be loaded.',
        });
      }
    };
    void load();
    return () => controller.abort();
  }, [
    isActive,
    requestKey,
    scope,
    tenantId,
    nodeId,
    profileId,
    rangeHours,
    resolution,
    pointLimit,
    eventLimit,
  ]);

  const isSettled = result.key === requestKey && requestKey !== '';
  const isSameScope = result.scope === scope && result.history != null;
  return {
    history: isSettled ? result.history : isSameScope ? result.history : null,
    error: isSettled ? result.error : null,
    isLoading: isActive && !isSettled,
    isStale: isActive && !isSettled && isSameScope,
  };
}
