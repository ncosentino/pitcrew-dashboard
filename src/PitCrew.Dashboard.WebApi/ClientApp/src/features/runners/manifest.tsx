import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const RunnersRoute = lazyFeature('runners', () => import('./RunnersRoute'));

/** Cross-fleet runner routes and navigation contribution. */
export const runnersManifest: FeatureManifest = {
  id: 'runners',
  navigation: [
    {
      label: 'Runners',
      description: 'Runner slots and current job correlation',
      path: '/tenants/:tenantId/runners',
      group: 'operate',
      order: 10,
      icon: 'runners',
    },
  ],
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
