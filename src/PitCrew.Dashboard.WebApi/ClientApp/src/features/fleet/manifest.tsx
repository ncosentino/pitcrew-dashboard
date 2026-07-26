import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const FleetOverviewPage = lazyFeature('fleet', () => import('./FleetOverviewPage'));
const FleetRouteGroup = lazyFeature('fleet', () => import('./FleetRouteGroup'));
const NodeDetailPage = lazyFeature('fleet', () => import('./NodeDetailPage'));
const ProfileDetailPage = lazyFeature('fleet', () => import('./pages/ProfileDetailPage'));

/** Fleet feature routes and navigation contribution. */
export const fleetManifest: FeatureManifest = {
  id: 'fleet',
  navigation: [{ label: 'Fleet', path: '/tenants/:tenantId/fleet' }],
  routes: [
    {
      element: <TenantRouteGuard minimumRole="viewer" />,
      children: [
        {
          element: <FleetRouteGroup />,
          children: [
            { path: 'tenants/:tenantId/fleet', element: <FleetOverviewPage /> },
            { path: 'tenants/:tenantId/nodes/:nodeId', element: <NodeDetailPage /> },
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
