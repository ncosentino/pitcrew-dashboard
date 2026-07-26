import { useMemo } from 'react';
import { Link, Outlet, useNavigate, useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { logout, useSession, type TenantRole } from '@/core/auth';
import { PitCrewBrand } from '@/core/branding/PitCrewBrand';
import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { ThemeToggle } from '@/core/theme/ThemeToggle';

const roleRanks: Record<TenantRole, number> = {
  viewer: 0,
  administrator: 1,
  owner: 2,
};

interface AuthenticatedShellProps {
  readonly features: ReadonlyArray<FeatureManifest>;
}

/** Provides persistent authenticated identity, tenant switching, and feature navigation. */
export function AuthenticatedShell({ features }: AuthenticatedShellProps) {
  const { session } = useSession();
  const { tenantId } = useParams();
  const navigate = useNavigate();
  const selectedTenant = session?.tenants.find((tenant) => tenant.tenantId === tenantId) ?? null;
  const navigation = useMemo(
    () => features.flatMap((feature) => feature.navigation ?? []),
    [features],
  );

  if (!session) return null;

  return (
    <main className="mx-auto flex min-h-screen max-w-7xl flex-col gap-6 px-4 py-8 sm:px-8">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div className="grid gap-2">
          <PitCrewBrand variant="compact" />
          <h1 className="text-3xl font-bold tracking-tight">Runner fleet</h1>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <ThemeToggle />
          {session.user.avatarUrl ? (
            <img
              className="size-9 rounded-full"
              src={session.user.avatarUrl}
              alt=""
              referrerPolicy="no-referrer"
            />
          ) : null}
          <div className="text-right text-sm">
            <div className="font-medium">{session.user.displayName}</div>
            <div className="text-muted-foreground">@{session.user.githubLogin}</div>
          </div>
          <Button
            type="button"
            variant="outline"
            onClick={() =>
              void logout(session.antiforgeryToken).then(() => globalThis.location.reload())
            }
          >
            Sign out
          </Button>
        </div>
      </header>

      <section className="flex flex-wrap items-center gap-3 rounded-lg border bg-muted/30 p-4">
        <label className="text-sm font-medium" htmlFor="tenant-context">
          Tenant
        </label>
        <select
          id="tenant-context"
          className="h-9 min-w-56 rounded-md border bg-background px-3 text-sm"
          value={selectedTenant?.tenantId ?? ''}
          onChange={(event) => navigate(`/tenants/${event.target.value}/fleet`)}
        >
          {session.tenants.length === 0 ? <option value="">No tenants</option> : null}
          {session.tenants.map((tenant) => (
            <option key={tenant.tenantId} value={tenant.tenantId}>
              {tenant.displayName} · {tenant.role}
            </option>
          ))}
        </select>
        {selectedTenant
          ? navigation
              .filter(
                (item) =>
                  !item.minimumTenantRole ||
                  roleRanks[selectedTenant.role] >= roleRanks[item.minimumTenantRole],
              )
              .map((item) => (
                <Button key={`${item.label}-${item.path}`} asChild variant="outline" size="sm">
                  <Link to={item.path.replace(':tenantId', selectedTenant.tenantId)}>
                    {item.label}
                  </Link>
                </Button>
              ))
          : null}
        {session.isSystemAdministrator ? (
          <Button asChild variant="outline" size="sm">
            <Link to="/admin/tenants">Tenant administration</Link>
          </Button>
        ) : null}
      </section>

      <Outlet />
    </main>
  );
}
