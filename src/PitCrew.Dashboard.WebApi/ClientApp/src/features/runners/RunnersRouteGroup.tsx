import { Outlet, useParams } from 'react-router-dom';

import { FleetProvider } from '@/core/fleet';

/** Provides the shared tenant fleet cache to the runners route. */
export default function RunnersRouteGroup() {
  const { tenantId = '' } = useParams();
  return (
    <FleetProvider tenantId={tenantId}>
      <Outlet />
    </FleetProvider>
  );
}
