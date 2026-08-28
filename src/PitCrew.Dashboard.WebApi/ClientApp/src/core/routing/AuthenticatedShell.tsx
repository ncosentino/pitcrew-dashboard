import { useEffect, useMemo, useRef, useState } from 'react';
import { MenuIcon, PanelLeftCloseIcon, PanelLeftOpenIcon } from 'lucide-react';
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
import { cn } from '@/lib/utils';

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
  readonly railMode: ShellRailMode;
  readonly canToggleRail: boolean;
  readonly onNavigate: () => void;
  readonly onTenantChange: (tenantId: string) => void;
  readonly onRailModeChange: (mode: ShellRailMode) => void;
}

const noOp = () => undefined;

type ShellRailMode = 'expanded' | 'compact';

/** Browser storage key for the remembered desktop shell rail mode. */
export const shellRailStorageKey = 'pitcrew-dashboard-shell-rail';

function readShellRailMode(): ShellRailMode {
  try {
    return globalThis.localStorage.getItem(shellRailStorageKey) === 'compact'
      ? 'compact'
      : 'expanded';
  } catch (error) {
    if (error instanceof DOMException) {
      console.warn('The dashboard could not read the saved shell rail mode.', error);
      return 'expanded';
    }
    throw error;
  }
}

function storeShellRailMode(mode: ShellRailMode): void {
  try {
    globalThis.localStorage.setItem(shellRailStorageKey, mode);
  } catch (error) {
    if (error instanceof DOMException) {
      console.warn('The dashboard could not save the shell rail mode.', error);
      return;
    }
    throw error;
  }
}

function ShellPanel({
  session,
  selectedTenant,
  navigation,
  pathname,
  tenantSelectId,
  railMode,
  canToggleRail,
  onNavigate,
  onTenantChange,
  onRailModeChange,
}: ShellPanelProps) {
  const compact = railMode === 'compact';
  const tenantDescriptionId = `${tenantSelectId}-description`;

  return (
    <div
      className={cn('flex h-full min-h-0 flex-col gap-6 p-5', compact && 'gap-4 px-3 py-4')}
      data-rail-mode={railMode}
    >
      <div className="flex min-w-0 items-center justify-between gap-2">
        <PitCrewBrand variant={compact ? 'mark' : 'compact'} />
        {canToggleRail ? (
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="size-9 shrink-0"
            aria-label={compact ? 'Expand primary navigation' : 'Collapse primary navigation'}
            aria-pressed={compact}
            onClick={() => onRailModeChange(compact ? 'expanded' : 'compact')}
          >
            {compact ? (
              <PanelLeftOpenIcon aria-hidden="true" />
            ) : (
              <PanelLeftCloseIcon aria-hidden="true" />
            )}
          </Button>
        ) : null}
      </div>
      <div
        className={cn('grid gap-2 rounded-lg border bg-sidebar-accent/35 p-3', compact && 'p-2')}
      >
        <div className="flex min-w-0 items-center justify-between gap-2">
          <label className="text-xs font-semibold" htmlFor={tenantSelectId}>
            Tenant
          </label>
          {selectedTenant ? (
            <span className="min-w-0 truncate text-[0.6875rem] font-medium text-sidebar-foreground/65 capitalize">
              {selectedTenant.role}
            </span>
          ) : null}
        </div>
        <select
          id={tenantSelectId}
          aria-describedby={tenantDescriptionId}
          className={cn(
            'h-9 min-w-0 w-full rounded-md border bg-background px-3 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring',
            compact && 'px-2 text-xs',
          )}
          title={
            selectedTenant ? `${selectedTenant.displayName} · ${selectedTenant.role}` : 'No tenants'
          }
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
        <p
          id={tenantDescriptionId}
          className={cn('text-xs leading-4 text-sidebar-foreground/65', compact && 'sr-only')}
        >
          Selecting another tenant opens its fleet overview.
        </p>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto">
        <ShellNavigation
          compact={compact}
          items={navigation}
          pathname={pathname}
          onNavigate={onNavigate}
        />
      </div>
      <div className="grid gap-3 border-t pt-4">
        <div className={cn('flex min-w-0 items-center gap-3', compact && 'justify-center')}>
          {session.user.avatarUrl ? (
            <img
              className="size-9 shrink-0 rounded-full"
              src={session.user.avatarUrl}
              alt=""
              referrerPolicy="no-referrer"
            />
          ) : null}
          <div className={cn('min-w-0 flex-1 text-sm', compact && 'sr-only')}>
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
  const [railMode, setRailMode] = useState<ShellRailMode>(readShellRailMode);
  const [incidentState, setIncidentState] = useState<{
    readonly tenantId: string;
    readonly count: number | null;
    readonly highestSeverity: 'critical' | 'warning' | null;
  } | null>(null);
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
      .map((item) => {
        const currentIncidentState =
          selectedTenant != null && incidentState?.tenantId === selectedTenant.tenantId
            ? incidentState
            : null;
        const badge = item.path.endsWith('/incidents')
          ? currentIncidentState?.count == null
            ? currentIncidentState
              ? {
                  label: '?',
                  accessibleLabel: 'Active incident count unavailable',
                  tone: 'neutral' as const,
                }
              : undefined
            : currentIncidentState.count > 0
              ? {
                  label: new Intl.NumberFormat(undefined).format(currentIncidentState.count),
                  accessibleLabel: `${currentIncidentState.count} active ${currentIncidentState.count === 1 ? 'incident' : 'incidents'}; highest severity ${currentIncidentState.highestSeverity ?? 'warning'}`,
                  tone:
                    currentIncidentState.highestSeverity === 'critical'
                      ? ('critical' as const)
                      : ('caution' as const),
                }
              : undefined
          : undefined;

        return {
          label: item.label,
          description: item.description,
          path: item.path.replace(':tenantId', selectedTenant?.tenantId ?? ''),
          group: item.group,
          order: item.order,
          icon: item.icon,
          activePaths: (item.activePathPatterns ?? [item.path]).map((path) =>
            path.replace(':tenantId', selectedTenant?.tenantId ?? ''),
          ),
          badge,
        };
      });
  }, [features, incidentState, selectedTenant, session]);
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
            count: null,
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
  const changeRailMode = (nextRailMode: ShellRailMode) => {
    setRailMode(nextRailMode);
    storeShellRailMode(nextRailMode);
  };

  return (
    <div
      className={cn(
        'min-h-screen md:grid motion-safe:transition-[grid-template-columns] motion-safe:duration-150',
        railMode === 'compact'
          ? 'md:grid-cols-[10rem_minmax(0,1fr)]'
          : 'md:grid-cols-[17rem_minmax(0,1fr)]',
      )}
    >
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
          railMode={railMode}
          canToggleRail
          onNavigate={noOp}
          onTenantChange={switchTenant}
          onRailModeChange={changeRailMode}
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
                railMode="expanded"
                canToggleRail={false}
                onNavigate={() => setIsMobileNavigationOpen(false)}
                onTenantChange={switchTenant}
                onRailModeChange={noOp}
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
