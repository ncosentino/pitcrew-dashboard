import { useCallback, useEffect, useMemo, useState } from 'react';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { PitCrewBrand } from '@/core/branding/PitCrewBrand';
import { ApiError } from '@/core/api/httpClient';
import { ThemeToggle } from '@/core/theme/ThemeToggle';
import {
  createTenant,
  getSession,
  logout,
  type DashboardSession,
  type TenantAccess,
} from '@/features/access/accessApi';
import { TenantAdministration } from '@/features/access/TenantAdministration';
import { FleetDashboard } from '@/features/fleet/FleetDashboard';

/** Coordinates authentication, tenant context, fleet visibility, and administration. */
function App() {
  const [session, setSession] = useState<DashboardSession | null>(null);
  const [selectedTenantId, setSelectedTenantId] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [isUnauthenticated, setIsUnauthenticated] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [newTenantId, setNewTenantId] = useState('');
  const [newTenantName, setNewTenantName] = useState('');

  const refreshSession = useCallback(async (signal: AbortSignal) => {
    try {
      const nextSession = await getSession(signal);
      setSession(nextSession);
      setSelectedTenantId((current) =>
        nextSession.tenants.some((tenant) => tenant.tenantId === current)
          ? current
          : (nextSession.tenants[0]?.tenantId ?? ''),
      );
      setIsUnauthenticated(false);
      setError(null);
    } catch (caught) {
      if (caught instanceof Error && caught.name === 'AbortError') return;
      if (caught instanceof ApiError && caught.status === 401) {
        setIsUnauthenticated(true);
        setSession(null);
      } else {
        setError(caught instanceof Error ? caught.message : 'Session could not be loaded.');
      }
    } finally {
      if (!signal.aborted) setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      void refreshSession(controller.signal);
    }, 0);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [refreshSession]);

  const selectedTenant = useMemo<TenantAccess | null>(
    () => session?.tenants.find((tenant) => tenant.tenantId === selectedTenantId) ?? null,
    [selectedTenantId, session],
  );

  if (isLoading) {
    return <main className="p-8 text-muted-foreground">Loading dashboard session…</main>;
  }

  if (isUnauthenticated) {
    return (
      <main className="mx-auto flex min-h-screen max-w-xl items-center px-4">
        <Card className="w-full">
          <CardHeader className="items-center text-center">
            <PitCrewBrand variant="hero" />
            <CardTitle className="mt-2 text-2xl">Sign in to PitCrew Dashboard</CardTitle>
            <CardDescription>
              Fleet data and connector administration require an authorized GitHub account.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild>
              <a href="/auth/login?returnUrl=/">Sign in with GitHub</a>
            </Button>
          </CardContent>
        </Card>
      </main>
    );
  }

  if (!session) {
    return <main className="p-8 text-red-700">{error ?? 'Dashboard session is unavailable.'}</main>;
  }

  const createNewTenant = async () => {
    try {
      await createTenant(newTenantId.trim(), newTenantName.trim(), session.antiforgeryToken);
      setNewTenantId('');
      setNewTenantName('');
      await refreshSession(new AbortController().signal);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Tenant could not be created.');
    }
  };

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

      {error ? (
        <div className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100">
          {error}
        </div>
      ) : null}

      <section className="flex flex-wrap items-center gap-3 rounded-lg border bg-muted/30 p-4">
        <label className="text-sm font-medium" htmlFor="tenant-context">
          Tenant
        </label>
        <select
          id="tenant-context"
          className="h-9 min-w-56 rounded-md border bg-background px-3 text-sm"
          value={selectedTenantId}
          onChange={(event) => setSelectedTenantId(event.target.value)}
        >
          {session.tenants.length === 0 ? <option value="">No tenants</option> : null}
          {session.tenants.map((tenant) => (
            <option key={tenant.tenantId} value={tenant.tenantId}>
              {tenant.displayName} · {tenant.role}
            </option>
          ))}
        </select>
      </section>

      {session.isSystemAdministrator ? (
        <Card>
          <CardHeader>
            <CardTitle>Create tenant</CardTitle>
            <CardDescription>
              Tenant IDs are stable lowercase route identifiers. You become the initial owner.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-3 sm:grid-cols-[1fr_1fr_auto]">
            <input
              className="h-9 rounded-md border bg-background px-3 text-sm"
              placeholder="tenant-id"
              value={newTenantId}
              onChange={(event) => setNewTenantId(event.target.value)}
            />
            <input
              className="h-9 rounded-md border bg-background px-3 text-sm"
              placeholder="Display name"
              value={newTenantName}
              onChange={(event) => setNewTenantName(event.target.value)}
            />
            <Button
              type="button"
              disabled={newTenantId.trim().length === 0 || newTenantName.trim().length === 0}
              onClick={() => void createNewTenant()}
            >
              Create
            </Button>
          </CardContent>
        </Card>
      ) : null}

      {selectedTenant ? (
        <>
          <FleetDashboard
            tenantId={selectedTenant.tenantId}
            canAdminister={
              selectedTenant.role === 'administrator' || selectedTenant.role === 'owner'
            }
            antiforgeryToken={session.antiforgeryToken}
          />
          {selectedTenant.role === 'owner' ? (
            <TenantAdministration
              tenantId={selectedTenant.tenantId}
              antiforgeryToken={session.antiforgeryToken}
            />
          ) : null}
        </>
      ) : (
        <Card>
          <CardHeader>
            <CardTitle>No tenant context</CardTitle>
            <CardDescription>
              A system administrator can create a tenant; otherwise ask a tenant owner to add your
              GitHub user after this first sign-in.
            </CardDescription>
          </CardHeader>
        </Card>
      )}
    </main>
  );
}

export default App;
