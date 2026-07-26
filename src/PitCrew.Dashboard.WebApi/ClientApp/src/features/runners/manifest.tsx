import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const RunnersRoute = lazyFeature('runners', () => import('./RunnersRoute'));
const RunnersRouteGroup = lazyFeature('runners', () => import('./RunnersRouteGroup'));

/** Cross-fleet runner routes and navigation contribution. */
export const runnersManifest: FeatureManifest = {
  id: 'runners',
  navigation: [{ label: 'Runners', path: '/tenants/:tenantId/runners' }],
  routes: [
    {
      element: <TenantRouteGuard minimumRole="viewer" />,
      children: [
        {
          element: <RunnersRouteGroup />,
          children: [{ path: 'tenants/:tenantId/runners', element: <RunnersRoute /> }],
        },
      ],
    },
  ],
};
