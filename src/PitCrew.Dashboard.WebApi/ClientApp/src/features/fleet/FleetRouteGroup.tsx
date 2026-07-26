import { Outlet, useParams } from 'react-router-dom';

import { FleetProvider } from '@/core/fleet';

/** Keeps one tenant fleet provider mounted across fleet-consuming sibling routes. */
export default function FleetRouteGroup() {
  const { tenantId = '' } = useParams();
  return (
    <FleetProvider tenantId={tenantId}>
      <Outlet />
    </FleetProvider>
  );
}
