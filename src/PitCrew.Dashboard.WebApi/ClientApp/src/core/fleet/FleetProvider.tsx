import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { ApiError } from '@/core/api/httpClient';

import { getFleet, type FleetResponse } from './fleetApi';
import { FleetContext } from './fleetContext';

const refreshIntervalMilliseconds = 5_000;

interface FleetProviderProps {
  readonly tenantId: string;
  readonly children: ReactNode;
}

interface FleetState {
  readonly tenantId: string;
  readonly fleet: FleetResponse | null;
  readonly error: string | null;
  readonly isLoading: boolean;
}

/** Owns the single polling lifecycle for one mounted tenant route group. */
export function FleetProvider({ tenantId, children }: FleetProviderProps) {
  const [state, setState] = useState<FleetState>({
    tenantId,
    fleet: null,
    error: null,
    isLoading: true,
  });
  const controller = useRef<AbortController | null>(null);
  const refreshSequence = useRef(0);

  const refreshNow = useCallback(async () => {
    controller.current?.abort();
    const currentController = new AbortController();
    controller.current = currentController;
    const sequence = ++refreshSequence.current;

    try {
      const fleet = await getFleet(tenantId, currentController.signal);
      if (currentController.signal.aborted || sequence !== refreshSequence.current) return;
      setState({ tenantId, fleet, error: null, isLoading: false });
    } catch (caught) {
      if (caught instanceof Error && caught.name === 'AbortError') return;
      if (sequence !== refreshSequence.current) return;
      const error =
        caught instanceof ApiError
          ? caught.message
          : caught instanceof Error
            ? caught.message
            : 'Fleet status could not be loaded.';
      setState((current) => ({
        tenantId,
        fleet: current.tenantId === tenantId ? current.fleet : null,
        error,
        isLoading: false,
      }));
    }
  }, [tenantId]);

  useEffect(() => {
    const initialTimer = window.setTimeout(() => {
      void refreshNow();
    }, 0);
    const refreshTimer = window.setInterval(() => {
      void refreshNow();
    }, refreshIntervalMilliseconds);

    return () => {
      controller.current?.abort();
      controller.current = null;
      window.clearTimeout(initialTimer);
      window.clearInterval(refreshTimer);
    };
  }, [refreshNow, tenantId]);

  const currentState =
    state.tenantId === tenantId ? state : { tenantId, fleet: null, error: null, isLoading: true };
  const value = useMemo(
    () => ({
      fleet: currentState.fleet,
      error: currentState.error,
      isLoading: currentState.isLoading,
      refreshNow,
    }),
    [currentState.error, currentState.fleet, currentState.isLoading, refreshNow],
  );

  return <FleetContext.Provider value={value}>{children}</FleetContext.Provider>;
}
