import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const RunnersRoute = lazyFeature('runners', () => import('./RunnersRoute'));

/** Cross-fleet runner routes and navigation contribution. */
export const runnersManifest: FeatureManifest = {
  id: 'runners',
  navigation: [{ label: 'Runners', path: '/tenants/:tenantId/runners' }],
  routePresentations: [
    {
      path: '/tenants/:tenantId/runners',
      title: 'Runners',
      breadcrumbs: [{ label: 'Runners' }],
    },
  ],
  routes: [
    {
      path: 'tenants/:tenantId/runners',
      element: (
        <TenantRouteGuard minimumRole="viewer">
          <RunnersRoute />
        </TenantRouteGuard>
      ),
    },
  ],
};
