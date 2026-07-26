import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';

import { ApiError } from '@/core/api/httpClient';

import { getSession, type DashboardSession } from './sessionApi';
import { SessionContext, type SessionStatus } from './sessionContext';

interface SessionProviderProps {
  readonly children: ReactNode;
}

/** Owns the single application session bootstrap and refresh surface. */
export function SessionProvider({ children }: SessionProviderProps) {
  const [status, setStatus] = useState<SessionStatus>('loading');
  const [session, setSession] = useState<DashboardSession | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadSession = useCallback(async (signal: AbortSignal) => {
    try {
      const nextSession = await getSession(signal);
      setSession(nextSession);
      setStatus('authenticated');
      setError(null);
    } catch (caught) {
      if (caught instanceof Error && caught.name === 'AbortError') return;
      setSession(null);
      if (caught instanceof ApiError && caught.status === 401) {
        setStatus('unauthenticated');
        setError(null);
      } else {
        setStatus('error');
        setError(caught instanceof Error ? caught.message : 'Session could not be loaded.');
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      void loadSession(controller.signal);
    }, 0);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [loadSession]);

  const refreshSession = useCallback(async () => {
    await loadSession(new AbortController().signal);
  }, [loadSession]);

  const value = useMemo(
    () => ({ status, session, error, refreshSession }),
    [error, refreshSession, session, status],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}
