import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const FleetOverviewPage = lazyFeature('fleet', () => import('./FleetOverviewPage'));
const IncidentsPage = lazyFeature('fleet', () => import('./IncidentsPage'));
const NodeDetailPage = lazyFeature('fleet', () => import('./NodeDetailPage'));
const ProfileDetailPage = lazyFeature('fleet', () => import('./pages/ProfileDetailPage'));

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
        '/tenants/:tenantId/nodes/:nodeId/profiles/:profileId',
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
      path: '/tenants/:tenantId/nodes/:nodeId',
      title: 'Node :nodeId',
      breadcrumbs: [
        { label: 'Fleet', path: '/tenants/:tenantId/fleet' },
        { label: 'Node :nodeId' },
      ],
    },
    {
      path: '/tenants/:tenantId/incidents',
      title: 'Operational incidents',
      breadcrumbs: [{ label: 'Incidents' }],
    },
    {
      path: '/tenants/:tenantId/nodes/:nodeId/profiles/:profileId',
      title: 'Profile :profileId',
      breadcrumbs: [
        { label: 'Fleet', path: '/tenants/:tenantId/fleet' },
        { label: 'Node :nodeId', path: '/tenants/:tenantId/nodes/:nodeId' },
        { label: 'Profile :profileId' },
      ],
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
          <NodeDetailPage />
        </TenantRouteGuard>
      ),
    },
    {
      path: 'tenants/:tenantId/incidents',
      element: (
        <TenantRouteGuard minimumRole="viewer">
          <IncidentsPage />
        </TenantRouteGuard>
      ),
    },
    {
      path: 'tenants/:tenantId/nodes/:nodeId/profiles/:profileId',
      element: (
        <TenantRouteGuard minimumRole="viewer">
          <ProfileDetailPage />
        </TenantRouteGuard>
      ),
    },
  ],
};
