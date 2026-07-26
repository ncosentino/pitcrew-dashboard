import { useContext } from 'react';

import { FleetContext, type FleetContextValue } from './fleetContext';

/** Returns the current tenant's shared fleet projection and refresh contract. */
export function useFleet(): FleetContextValue {
  const value = useContext(FleetContext);
  if (!value) throw new Error('useFleet must be used within FleetProvider.');
  return value;
}
