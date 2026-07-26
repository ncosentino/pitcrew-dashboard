import { useMemo, useState } from 'react';
import { MenuIcon } from 'lucide-react';
import { Outlet, useLocation, useNavigate, useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from '@/components/ui/sheet';
import {
  hasMinimumTenantRole,
  logout,
  useSession,
  type DashboardSession,
  type TenantAccess,
} from '@/core/auth';
import { PitCrewBrand } from '@/core/branding/PitCrewBrand';
import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { ThemeToggle } from '@/core/theme/ThemeToggle';

import { Breadcrumbs } from './Breadcrumbs';
import { formatRouteLabel, matchRoutePresentation } from './routePresentation';
import { ShellNavigation, type ShellNavigationItem } from './ShellNavigation';

interface AuthenticatedShellProps {
  readonly features: ReadonlyArray<FeatureManifest>;
}

interface ShellPanelProps {
  readonly session: DashboardSession;
  readonly selectedTenant: TenantAccess | null;
  readonly navigation: ReadonlyArray<ShellNavigationItem>;
  readonly pathname: string;
  readonly tenantSelectId: string;
  readonly onNavigate: () => void;
  readonly onTenantChange: (tenantId: string) => void;
}

const keepPanelOpen = () => undefined;

function ShellPanel({
  session,
  selectedTenant,
  navigation,
  pathname,
  tenantSelectId,
  onNavigate,
  onTenantChange,
}: ShellPanelProps) {
  return (
    <div className="flex h-full min-h-0 flex-col gap-6 p-5">
      <PitCrewBrand variant="compact" />
      <div className="grid gap-2">
        <label className="text-sm font-medium" htmlFor={tenantSelectId}>
          Tenant
        </label>
        <select
          id={tenantSelectId}
          className="h-9 min-w-0 rounded-md border bg-background px-3 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
          value={selectedTenant?.tenantId ?? ''}
          onChange={(event) => onTenantChange(event.target.value)}
        >
          {session.tenants.length === 0 ? <option value="">No tenants</option> : null}
          {session.tenants.map((tenant) => (
            <option key={tenant.tenantId} value={tenant.tenantId}>
              {tenant.displayName} · {tenant.role}
            </option>
          ))}
        </select>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto">
        <ShellNavigation items={navigation} pathname={pathname} onNavigate={onNavigate} />
      </div>
      <div className="grid gap-3 border-t pt-4">
        <div className="flex min-w-0 items-center gap-3">
          {session.user.avatarUrl ? (
            <img
              className="size-9 shrink-0 rounded-full"
              src={session.user.avatarUrl}
              alt=""
              referrerPolicy="no-referrer"
            />
          ) : null}
          <div className="min-w-0 flex-1 text-sm">
            <div className="truncate font-medium">{session.user.displayName}</div>
            <div className="truncate text-muted-foreground">@{session.user.githubLogin}</div>
          </div>
          <ThemeToggle />
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
    </div>
  );
}

/** Provides persistent authenticated identity, tenant switching, and feature navigation. */
export function AuthenticatedShell({ features }: AuthenticatedShellProps) {
  const { session } = useSession();
  const { tenantId } = useParams();
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const [isMobileNavigationOpen, setIsMobileNavigationOpen] = useState(false);
  const selectedTenant =
    session?.tenants.find((tenant) => tenant.tenantId === tenantId) ??
    (tenantId === undefined ? (session?.tenants[0] ?? null) : null);
  const navigation = useMemo(() => {
    if (!session) return [];

    return features
      .flatMap((feature) => feature.navigation ?? [])
      .filter((item) => {
        if (item.requiresSystemAdministrator && !session.isSystemAdministrator) return false;
        if (!item.path.includes(':tenantId')) return true;
        if (!selectedTenant) return false;
        return (
          !item.minimumTenantRole ||
          hasMinimumTenantRole(selectedTenant.role, item.minimumTenantRole)
        );
      })
      .map((item) => ({
        label: item.label,
        path: item.path.replace(':tenantId', selectedTenant?.tenantId ?? ''),
        activePaths: (item.activePathPatterns ?? [item.path]).map((path) =>
          path.replace(':tenantId', selectedTenant?.tenantId ?? ''),
        ),
      }));
  }, [features, selectedTenant, session]);
  const routePresentation = useMemo(
    () => matchRoutePresentation(features, pathname),
    [features, pathname],
  );

  if (!session) return null;

  const switchTenant = (nextTenantId: string) => {
    if (!nextTenantId) return;
    setIsMobileNavigationOpen(false);
    navigate(`/tenants/${nextTenantId}/fleet`);
  };

  return (
    <div className="min-h-screen md:grid md:grid-cols-[17rem_minmax(0,1fr)]">
      <a
        className="fixed top-3 left-3 z-[100] -translate-y-20 rounded-md bg-background px-4 py-2 font-medium shadow-md outline-none focus:translate-y-0 focus-visible:ring-2 focus-visible:ring-ring"
        href="#main-content"
      >
        Skip to content
      </a>

      <aside className="sticky top-0 hidden h-screen border-r bg-sidebar text-sidebar-foreground md:block">
        <ShellPanel
          session={session}
          selectedTenant={selectedTenant}
          navigation={navigation}
          pathname={pathname}
          tenantSelectId="tenant-context"
          onNavigate={keepPanelOpen}
          onTenantChange={switchTenant}
        />
      </aside>

      <div className="min-w-0">
        <header className="flex h-16 items-center justify-between gap-3 border-b bg-background/95 px-4 backdrop-blur md:hidden">
          <PitCrewBrand variant="compact" />
          <Sheet open={isMobileNavigationOpen} onOpenChange={setIsMobileNavigationOpen}>
            <SheetTrigger asChild>
              <Button type="button" variant="outline" size="icon" aria-label="Open navigation">
                <MenuIcon aria-hidden="true" />
              </Button>
            </SheetTrigger>
            <SheetContent side="left" className="w-[min(20rem,85vw)] p-0">
              <SheetHeader className="sr-only">
                <SheetTitle>Navigation</SheetTitle>
                <SheetDescription>Tenant, navigation, and account controls</SheetDescription>
              </SheetHeader>
              <ShellPanel
                session={session}
                selectedTenant={selectedTenant}
                navigation={navigation}
                pathname={pathname}
                tenantSelectId="tenant-context-mobile"
                onNavigate={() => setIsMobileNavigationOpen(false)}
                onTenantChange={switchTenant}
              />
            </SheetContent>
          </Sheet>
        </header>

        <main
          id="main-content"
          className="mx-auto grid min-w-0 max-w-7xl gap-6 px-4 py-6 sm:px-8 sm:py-8"
          tabIndex={-1}
        >
          <div className="grid gap-2">
            <Breadcrumbs match={routePresentation} />
            <h1 className="text-3xl font-bold tracking-tight">
              {formatRouteLabel(routePresentation.presentation.title, routePresentation.params)}
            </h1>
          </div>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
