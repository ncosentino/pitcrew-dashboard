import { useEffect, useState } from 'react';

import {
  getHistoryCapabilities,
  getNodeHistory,
  getProfileHistory,
  type HistoryCapabilities,
  type NodeHistoryResponse,
} from './historyApi';

/** Bounded history request issued by one node or profile history view. */
export interface FleetHistoryRequest {
  readonly tenantId: string;
  readonly nodeId: string;
  readonly profileId: string | null;
  readonly rangeHours: number;
  readonly resolution: 'raw' | 'hourly';
  readonly pointLimit: number | null;
  readonly eventLimit: number | null;
  readonly diagnosticLimit: number | null;
  readonly enabled: boolean;
}

/** Load state of the server-advertised history capabilities. */
export interface HistoryCapabilitiesState {
  readonly capabilities: HistoryCapabilities | null;
  readonly error: string | null;
  readonly isLoading: boolean;
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
  const {
    tenantId,
    nodeId,
    profileId,
    rangeHours,
    resolution,
    pointLimit,
    eventLimit,
    diagnosticLimit,
    enabled,
  } = request;
  const isActive = enabled && tenantId !== '' && nodeId !== '';
  const scope = `${tenantId}|${nodeId}|${profileId ?? ''}`;
  const requestKey = isActive
    ? `${scope}|${rangeHours}|${resolution}|${pointLimit}|${eventLimit}|${diagnosticLimit}`
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
      ...(pointLimit == null ? {} : { points: pointLimit }),
      ...(eventLimit == null ? {} : { events: eventLimit }),
      ...(diagnosticLimit == null ? {} : { diagnostics: diagnosticLimit }),
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
    diagnosticLimit,
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

/**
 * Loads the history limits the server advertises for one tenant.
 *
 * The client designs its ranges from these limits instead of assuming fixed presets, so a server
 * configured with a shorter maximum range or lower caps never receives a request it must reject.
 */
export function useHistoryCapabilities(
  tenantId: string,
  enabled: boolean,
): HistoryCapabilitiesState {
  const isActive = enabled && tenantId !== '';
  const [state, setState] = useState<{
    readonly key: string;
    readonly capabilities: HistoryCapabilities | null;
    readonly error: string | null;
  }>({ key: '', capabilities: null, error: null });

  useEffect(() => {
    if (!isActive) return undefined;
    const controller = new AbortController();
    const load = async () => {
      try {
        const capabilities = await getHistoryCapabilities(tenantId, controller.signal);
        if (!controller.signal.aborted) {
          setState({ key: tenantId, capabilities, error: null });
        }
      } catch (caught) {
        if (controller.signal.aborted) return;
        setState({
          key: tenantId,
          capabilities: null,
          error:
            caught instanceof Error ? caught.message : 'History capabilities could not be loaded.',
        });
      }
    };
    void load();
    return () => controller.abort();
  }, [isActive, tenantId]);

  const isSettled = state.key === tenantId && tenantId !== '';
  return {
    capabilities: isSettled ? state.capabilities : null,
    error: isSettled ? state.error : null,
    isLoading: isActive && !isSettled,
  };
}
