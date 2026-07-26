import { createContext } from 'react';

import type { FleetResponse } from './fleetApi';

export interface FleetContextValue {
  readonly fleet: FleetResponse | null;
  readonly error: string | null;
  readonly isLoading: boolean;
  readonly refreshNow: () => Promise<void>;
}

export const FleetContext = createContext<FleetContextValue | null>(null);
