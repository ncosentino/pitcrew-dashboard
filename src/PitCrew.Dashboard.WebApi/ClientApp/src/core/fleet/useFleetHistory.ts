import { useEffect, useState } from 'react';

import { getNodeHistory, getProfileHistory, type NodeHistoryResponse } from './historyApi';

/** Bounded history request issued by one node or profile history view. */
export interface FleetHistoryRequest {
  readonly tenantId: string;
  readonly nodeId: string;
  readonly profileId: string | null;
  readonly rangeHours: number;
  readonly resolution: 'raw' | 'hourly';
  readonly enabled: boolean;
}

/** Load state of one bounded history request. */
export interface FleetHistoryState {
  readonly history: NodeHistoryResponse | null;
  readonly error: string | null;
  readonly isLoading: boolean;
}

interface HistoryResult {
  readonly key: string;
  readonly history: NodeHistoryResponse | null;
  readonly error: string | null;
}

const idle: HistoryResult = { key: '', history: null, error: null };

/**
 * Loads bounded retained history for one node or profile.
 *
 * The range is always bounded by the caller and re-requested whenever the range or resolution
 * changes, so no unbounded query is ever issued. The settled result is keyed by the request it
 * answered, so a stale response never replaces the current range.
 */
export function useFleetHistory(request: FleetHistoryRequest): FleetHistoryState {
  const { tenantId, nodeId, profileId, rangeHours, resolution, enabled } = request;
  const isActive = enabled && tenantId !== '' && nodeId !== '';
  const requestKey = isActive
    ? `${tenantId}|${nodeId}|${profileId ?? ''}|${rangeHours}|${resolution}`
    : '';
  const [result, setResult] = useState<HistoryResult>(idle);

  useEffect(() => {
    if (!isActive) return undefined;
    const controller = new AbortController();
    const to = new Date();
    const from = new Date(to.getTime() - rangeHours * 60 * 60 * 1000);
    const query = { from: from.toISOString(), to: to.toISOString(), resolution };
    const load = async () => {
      try {
        const history =
          profileId == null
            ? await getNodeHistory(tenantId, nodeId, query, controller.signal)
            : await getProfileHistory(tenantId, nodeId, profileId, query, controller.signal);
        if (!controller.signal.aborted) setResult({ key: requestKey, history, error: null });
      } catch (caught) {
        if (controller.signal.aborted) return;
        setResult({
          key: requestKey,
          history: null,
          error:
            caught instanceof Error ? caught.message : 'Historical telemetry could not be loaded.',
        });
      }
    };
    void load();
    return () => controller.abort();
  }, [isActive, requestKey, tenantId, nodeId, profileId, rangeHours, resolution]);

  const isSettled = result.key === requestKey && requestKey !== '';
  return {
    history: isSettled ? result.history : null,
    error: isSettled ? result.error : null,
    isLoading: isActive && !isSettled,
  };
}
