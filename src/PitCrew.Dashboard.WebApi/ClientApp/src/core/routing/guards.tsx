import { Navigate, Outlet, useParams } from 'react-router-dom';

import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { hasMinimumTenantRole, useSession, type TenantRole } from '@/core/auth';

interface TenantRouteGuardProps {
  readonly minimumRole: TenantRole;
}

/** Rejects unknown tenant IDs and tenant roles below the route requirement. */
export function TenantRouteGuard({ minimumRole }: TenantRouteGuardProps) {
  const { tenantId } = useParams();
  const { session } = useSession();
  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);

  if (!tenant) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Tenant unavailable</CardTitle>
          <CardDescription>
            This tenant does not exist or your account is not authorized to access it.
          </CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (!hasMinimumTenantRole(tenant.role, minimumRole)) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Insufficient tenant role</CardTitle>
          <CardDescription>
            This page requires the {minimumRole} role. The API remains the final authorization
            authority.
          </CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return <Outlet />;
}

/** Restricts a route to dashboard system administrators. */
export function SystemAdministratorGuard() {
  const { session } = useSession();
  return session?.isSystemAdministrator ? <Outlet /> : <Navigate to="/no-access" replace />;
}
