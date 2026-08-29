import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const SupportPage = lazyFeature('support', () => import('./SupportPage'));

/** Support-plane routes and navigation contribution. */
export const supportManifest: FeatureManifest = {
  id: 'support',
  navigation: [
    {
      label: 'Support',
      description: 'Read-only diagnostics and support sessions',
      path: '/tenants/:tenantId/support',
      group: 'operate',
      order: 30,
      icon: 'support',
      minimumTenantRole: 'administrator',
      activePathPatterns: ['/tenants/:tenantId/support/*'],
    },
  ],
  routePresentations: [
    {
      path: '/tenants/:tenantId/support/*',
      title: 'Support diagnostics',
      breadcrumbs: [{ label: 'Support' }],
    },
  ],
  routes: [
    {
      path: 'tenants/:tenantId/support/*',
      element: (
        <TenantRouteGuard minimumRole="administrator">
          <SupportPage />
        </TenantRouteGuard>
      ),
    },
  ],
};
