import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const FleetRoute = lazyFeature('fleet', () => import('./FleetRoute'));
const FleetRouteGroup = lazyFeature('fleet', () => import('./FleetRouteGroup'));
const NodePlaceholderPage = lazyFeature('fleet', () => import('./NodePlaceholderPage'));
const ProfilePlaceholderPage = lazyFeature('fleet', () => import('./ProfilePlaceholderPage'));

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
            { path: 'tenants/:tenantId/fleet', element: <FleetRoute /> },
            { path: 'tenants/:tenantId/nodes/:nodeId', element: <NodePlaceholderPage /> },
            {
              path: 'tenants/:tenantId/nodes/:nodeId/profiles/:profileId',
              element: <ProfilePlaceholderPage />,
            },
          ],
        },
      ],
    },
  ],
};
