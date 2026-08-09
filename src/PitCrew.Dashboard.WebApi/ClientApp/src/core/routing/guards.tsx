import type { ReactNode } from 'react';
import { Navigate, Outlet, useParams } from 'react-router-dom';

import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { hasMinimumTenantRole, useSession, type TenantRole } from '@/core/auth';

interface TenantRouteGuardProps {
  readonly minimumRole: TenantRole;
  readonly children?: ReactNode;
}

/** Rejects unknown tenant IDs and tenant roles below the route requirement. */
export function TenantRouteGuard({ minimumRole, children }: TenantRouteGuardProps) {
  const { tenantId } = useParams();
  const { session } = useSession();
  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);

  if (!tenant) {
    return (
      <Card>
        <CardHeader>
          <CardTitle as="h2">Tenant unavailable</CardTitle>
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
          <CardTitle as="h2">Insufficient tenant role</CardTitle>
          <CardDescription>
            This page requires the {minimumRole} role. The API remains the final authorization
            authority.
          </CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return children ?? <Outlet />;
}

/** Restricts a route to dashboard system administrators. */
export function SystemAdministratorGuard({ children }: { readonly children?: ReactNode }) {
  const { session } = useSession();
  return session?.isSystemAdministrator ? (
    (children ?? <Outlet />)
  ) : (
    <Navigate to="/no-access" replace />
  );
}
