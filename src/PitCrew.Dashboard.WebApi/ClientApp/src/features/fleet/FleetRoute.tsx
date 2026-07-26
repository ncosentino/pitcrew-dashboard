import { useParams } from 'react-router-dom';

import { useSession } from '@/core/auth';

import { FleetDashboard } from './FleetDashboard';

/** Hosts the existing fleet dashboard at its tenant-scoped route. */
export default function FleetRoute() {
  const { tenantId = '' } = useParams();
  const { session } = useSession();
  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);
  if (!session || !tenant) return null;

  return (
    <FleetDashboard
      tenantId={tenantId}
      canAdminister={tenant.role === 'administrator' || tenant.role === 'owner'}
      antiforgeryToken={session.antiforgeryToken}
    />
  );
}
