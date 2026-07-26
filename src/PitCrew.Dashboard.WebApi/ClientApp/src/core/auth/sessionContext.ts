import { createContext } from 'react';

import type { DashboardSession } from './sessionApi';

export type SessionStatus = 'loading' | 'authenticated' | 'unauthenticated' | 'error';

export interface SessionContextValue {
  readonly status: SessionStatus;
  readonly session: DashboardSession | null;
  readonly error: string | null;
  readonly refreshSession: () => Promise<void>;
}

export const SessionContext = createContext<SessionContextValue | null>(null);
