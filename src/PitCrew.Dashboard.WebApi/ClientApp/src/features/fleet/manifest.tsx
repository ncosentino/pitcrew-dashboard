import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const FleetRoute = lazyFeature('fleet', () => import('./FleetRoute'));
const FleetRouteGroup = lazyFeature('fleet', () => import('./FleetRouteGroup'));
const NodePlaceholderPage = lazyFeature('fleet', () => import('./NodePlaceholderPage'));
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
      element: <TenantRouteGuard minimumRole="viewer" />,
      children: [
        {
          element: <FleetRouteGroup />,
          children: [
            { path: 'tenants/:tenantId/fleet', element: <FleetRoute /> },
            { path: 'tenants/:tenantId/nodes/:nodeId', element: <NodePlaceholderPage /> },
            {
              path: 'tenants/:tenantId/nodes/:nodeId/profiles/:profileId',
              element: <ProfileDetailPage />,
            },
          ],
        },
      ],
    },
  ],
};
