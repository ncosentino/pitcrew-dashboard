import { useEffect, useMemo, useRef, useState } from 'react';
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
import { FleetProvider, getActiveIncidents } from '@/core/fleet';
import { ThemeToggle } from '@/core/theme/ThemeToggle';
import { PageHeader } from '@/core/ui/PageHeader';

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

const noOp = () => undefined;

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
  const mainContent = useRef<HTMLElement>(null);
  const [isMobileNavigationOpen, setIsMobileNavigationOpen] = useState(false);
  const [incidentState, setIncidentState] = useState<{
    readonly tenantId: string;
    readonly count: number;
    readonly highestSeverity: 'critical' | 'warning' | null;
  } | null>(null);
  const selectedTenant =
    session?.tenants.find((tenant) => tenant.tenantId === tenantId) ??
    (tenantId === undefined ? (session?.tenants[0] ?? null) : null);
  const activeIncidentCount =
    incidentState != null &&
    selectedTenant != null &&
    incidentState.tenantId === selectedTenant.tenantId
      ? incidentState.count
      : 0;
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
        badge:
          activeIncidentCount > 0 && item.path.endsWith('/incidents')
            ? {
                label: new Intl.NumberFormat(undefined).format(activeIncidentCount),
                accessibleLabel: `${activeIncidentCount} active ${activeIncidentCount === 1 ? 'incident' : 'incidents'}; highest severity ${incidentState?.highestSeverity ?? 'warning'}`,
                tone:
                  incidentState?.highestSeverity === 'critical'
                    ? ('critical' as const)
                    : ('caution' as const),
              }
            : undefined,
      }));
  }, [activeIncidentCount, features, incidentState, selectedTenant, session]);
  const routePresentation = useMemo(
    () => matchRoutePresentation(features, pathname),
    [features, pathname],
  );
  const usesFleetData =
    selectedTenant !== null &&
    /^\/tenants\/[^/]+\/(?:fleet(?:\/|$)|nodes(?:\/|$)|runners(?:\/|$))/.test(pathname);

  const pageTitle = formatRouteLabel(
    routePresentation.presentation.title,
    routePresentation.params,
  );

  useEffect(() => {
    document.title = `${pageTitle} · PitCrew Dashboard`;
  }, [pageTitle]);

  useEffect(() => {
    mainContent.current?.focus({ preventScroll: true });
  }, [pathname]);

  useEffect(() => {
    if (!selectedTenant) {
      return;
    }
    const controller = new AbortController();
    const load = async () => {
      try {
        const incidents = await getActiveIncidents(selectedTenant.tenantId, controller.signal);
        if (!controller.signal.aborted) {
          setIncidentState({
            tenantId: selectedTenant.tenantId,
            count: incidents.length,
            highestSeverity: incidents.some((incident) => incident.severity === 'critical')
              ? 'critical'
              : incidents.length > 0
                ? 'warning'
                : null,
          });
        }
      } catch (caught) {
        if (caught instanceof Error && caught.name === 'AbortError') return;
        if (!controller.signal.aborted) {
          setIncidentState({
            tenantId: selectedTenant.tenantId,
            count: 0,
            highestSeverity: null,
          });
        }
      }
    };
    void load();
    const timer = globalThis.setInterval(() => void load(), 30_000);
    return () => {
      controller.abort();
      globalThis.clearInterval(timer);
    };
  }, [selectedTenant]);

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
          onNavigate={noOp}
          onTenantChange={switchTenant}
        />
      </aside>

      <div className="min-w-0">
        <header className="flex h-16 items-center justify-between gap-3 border-b bg-background/95 px-4 pt-[env(safe-area-inset-top,0px)] backdrop-blur md:hidden">
          <PitCrewBrand variant="compact" />
          <Sheet open={isMobileNavigationOpen} onOpenChange={setIsMobileNavigationOpen}>
            <SheetTrigger asChild>
              <Button
                type="button"
                variant="outline"
                size="icon"
                className="size-11"
                aria-label="Open navigation"
              >
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
          ref={mainContent}
          id="main-content"
          className="mx-auto grid min-w-0 max-w-7xl gap-6 px-4 py-6 sm:px-8 sm:py-8"
          tabIndex={-1}
        >
          <PageHeader breadcrumbs={<Breadcrumbs match={routePresentation} />} title={pageTitle} />
          {usesFleetData ? (
            <FleetProvider tenantId={selectedTenant.tenantId}>
              <Outlet />
            </FleetProvider>
          ) : (
            <Outlet />
          )}
        </main>
      </div>
    </div>
  );
}
