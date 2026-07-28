import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const FleetOverviewPage = lazyFeature('fleet', () => import('./FleetOverviewPage'));
const IncidentsPage = lazyFeature('fleet', () => import('./IncidentsPage'));
const NodeDetailLayout = lazyFeature('fleet', async () => {
  const module = await import('./pages/NodePages');
  return { default: module.NodeDetailLayout };
});
const NodeOverviewPage = lazyFeature('fleet', async () => {
  const module = await import('./pages/NodePages');
  return { default: module.NodeOverviewPage };
});
const NodeHistoryPage = lazyFeature('fleet', async () => {
  const module = await import('./pages/NodePages');
  return { default: module.NodeHistoryPage };
});
const NodeAdministrationPage = lazyFeature('fleet', async () => {
  const module = await import('./pages/NodePages');
  return { default: module.NodeAdministrationPage };
});
const ProfileDetailLayout = lazyFeature('fleet', async () => {
  const module = await import('./pages/ProfilePages');
  return { default: module.ProfileDetailLayout };
});
const ProfileOverviewPage = lazyFeature('fleet', async () => {
  const module = await import('./pages/ProfilePages');
  return { default: module.ProfileOverviewPage };
});
const ProfileCapacityPage = lazyFeature('fleet', async () => {
  const module = await import('./pages/ProfilePages');
  return { default: module.ProfileCapacityPage };
});
const ProfileWorkersPage = lazyFeature('fleet', async () => {
  const module = await import('./pages/ProfilePages');
  return { default: module.ProfileWorkersPage };
});
const ProfileDiagnosticsPage = lazyFeature('fleet', async () => {
  const module = await import('./pages/ProfilePages');
  return { default: module.ProfileDiagnosticsPage };
});
const ProfileHistoryPage = lazyFeature('fleet', async () => {
  const module = await import('./pages/ProfilePages');
  return { default: module.ProfileHistoryPage };
});
const ProfileRecoveryPage = lazyFeature('fleet', async () => {
  const module = await import('./pages/ProfilePages');
  return { default: module.ProfileRecoveryPage };
});

const nodePath = '/tenants/:tenantId/nodes/:nodeId';
const profilePath = `${nodePath}/profiles/:profileId`;

function nodeBreadcrumbs(section?: string) {
  return [
    { label: 'Fleet', path: '/tenants/:tenantId/fleet' },
    ...(section === undefined
      ? [{ label: 'Node :nodeId' }]
      : [{ label: 'Node :nodeId', path: nodePath }, { label: section }]),
  ];
}

function profileBreadcrumbs(section?: string) {
  return [
    { label: 'Fleet', path: '/tenants/:tenantId/fleet' },
    { label: 'Node :nodeId', path: nodePath },
    ...(section === undefined
      ? [{ label: 'Profile :profileId' }]
      : [{ label: 'Profile :profileId', path: profilePath }, { label: section }]),
  ];
}

/** Fleet feature routes and navigation contribution. */
export const fleetManifest: FeatureManifest = {
  id: 'fleet',
  navigation: [
    {
      label: 'Fleet',
      path: '/tenants/:tenantId/fleet',
      activePathPatterns: [
        '/tenants/:tenantId/fleet',
        '/tenants/:tenantId/nodes/:nodeId',
        '/tenants/:tenantId/nodes/:nodeId/history',
        '/tenants/:tenantId/nodes/:nodeId/administration',
        '/tenants/:tenantId/nodes/:nodeId/profiles/:profileId',
        '/tenants/:tenantId/nodes/:nodeId/profiles/:profileId/capacity',
        '/tenants/:tenantId/nodes/:nodeId/profiles/:profileId/workers',
        '/tenants/:tenantId/nodes/:nodeId/profiles/:profileId/diagnostics',
        '/tenants/:tenantId/nodes/:nodeId/profiles/:profileId/history',
        '/tenants/:tenantId/nodes/:nodeId/profiles/:profileId/recovery',
      ],
    },
    {
      label: 'Incidents',
      path: '/tenants/:tenantId/incidents',
      activePathPatterns: ['/tenants/:tenantId/incidents'],
    },
  ],
  routePresentations: [
    {
      path: '/tenants/:tenantId/fleet',
      title: 'Runner fleet',
      breadcrumbs: [{ label: 'Fleet' }],
    },
    {
      path: nodePath,
      title: 'Node :nodeId overview',
      breadcrumbs: nodeBreadcrumbs(),
    },
    {
      path: `${nodePath}/history`,
      title: 'Node :nodeId history',
      breadcrumbs: nodeBreadcrumbs('History'),
    },
    {
      path: `${nodePath}/administration`,
      title: 'Node :nodeId administration',
      breadcrumbs: nodeBreadcrumbs('Administration'),
    },
    {
      path: '/tenants/:tenantId/incidents',
      title: 'Operational incidents',
      breadcrumbs: [{ label: 'Incidents' }],
    },
    {
      path: profilePath,
      title: 'Profile :profileId overview',
      breadcrumbs: profileBreadcrumbs(),
    },
    {
      path: `${profilePath}/capacity`,
      title: 'Profile :profileId capacity',
      breadcrumbs: profileBreadcrumbs('Capacity'),
    },
    {
      path: `${profilePath}/workers`,
      title: 'Profile :profileId workers',
      breadcrumbs: profileBreadcrumbs('Workers'),
    },
    {
      path: `${profilePath}/diagnostics`,
      title: 'Profile :profileId diagnostics',
      breadcrumbs: profileBreadcrumbs('Diagnostics'),
    },
    {
      path: `${profilePath}/history`,
      title: 'Profile :profileId history',
      breadcrumbs: profileBreadcrumbs('History'),
    },
    {
      path: `${profilePath}/recovery`,
      title: 'Profile :profileId recovery',
      breadcrumbs: profileBreadcrumbs('Recovery'),
    },
  ],
  routes: [
    {
      path: 'tenants/:tenantId/fleet',
      element: (
        <TenantRouteGuard minimumRole="viewer">
          <FleetOverviewPage />
        </TenantRouteGuard>
      ),
    },
    {
      path: 'tenants/:tenantId/nodes/:nodeId',
      element: (
        <TenantRouteGuard minimumRole="viewer">
          <NodeDetailLayout />
        </TenantRouteGuard>
      ),
      children: [
        { index: true, element: <NodeOverviewPage /> },
        { path: 'history', element: <NodeHistoryPage /> },
        {
          path: 'administration',
          element: (
            <TenantRouteGuard minimumRole="administrator">
              <NodeAdministrationPage />
            </TenantRouteGuard>
          ),
        },
      ],
    },
    {
      path: 'tenants/:tenantId/nodes/:nodeId/profiles/:profileId',
      element: (
        <TenantRouteGuard minimumRole="viewer">
          <ProfileDetailLayout />
        </TenantRouteGuard>
      ),
      children: [
        { index: true, element: <ProfileOverviewPage /> },
        { path: 'capacity', element: <ProfileCapacityPage /> },
        { path: 'workers', element: <ProfileWorkersPage /> },
        { path: 'diagnostics', element: <ProfileDiagnosticsPage /> },
        { path: 'history', element: <ProfileHistoryPage /> },
        { path: 'recovery', element: <ProfileRecoveryPage /> },
      ],
    },
    {
      path: 'tenants/:tenantId/incidents',
      element: (
        <TenantRouteGuard minimumRole="viewer">
          <IncidentsPage />
        </TenantRouteGuard>
      ),
    },
  ],
};
