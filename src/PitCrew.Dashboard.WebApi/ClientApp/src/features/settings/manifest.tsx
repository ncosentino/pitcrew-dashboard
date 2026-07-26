import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const GeneralSettingsPage = lazyFeature('settings', async () => {
  const module = await import('./SettingsPages');
  return { default: module.GeneralSettingsPage };
});
const AccessSettingsPage = lazyFeature('settings', async () => {
  const module = await import('./SettingsPages');
  return { default: module.AccessSettingsPage };
});
const EnrollmentSettingsPage = lazyFeature('settings', async () => {
  const module = await import('./SettingsPages');
  return { default: module.EnrollmentSettingsPage };
});

/** Settings feature routes and navigation contribution. */
export const settingsManifest: FeatureManifest = {
  id: 'settings',
  navigation: [
    {
      label: 'Settings',
      path: '/tenants/:tenantId/settings/general',
      minimumTenantRole: 'owner',
      activePathPatterns: [
        '/tenants/:tenantId/settings/general',
        '/tenants/:tenantId/settings/access',
      ],
    },
    {
      label: 'Enrollment',
      path: '/tenants/:tenantId/settings/enrollment',
      minimumTenantRole: 'administrator',
    },
  ],
  routePresentations: [
    {
      path: '/tenants/:tenantId/settings/general',
      title: 'Tenant settings',
      breadcrumbs: [{ label: 'Settings' }, { label: 'General' }],
    },
    {
      path: '/tenants/:tenantId/settings/access',
      title: 'Tenant access',
      breadcrumbs: [
        { label: 'Settings', path: '/tenants/:tenantId/settings/general' },
        { label: 'Access' },
      ],
    },
    {
      path: '/tenants/:tenantId/settings/enrollment',
      title: 'Connector enrollment',
      breadcrumbs: [
        { label: 'Settings', path: '/tenants/:tenantId/settings/general' },
        { label: 'Enrollment' },
      ],
    },
  ],
  routes: [
    {
      path: 'tenants/:tenantId/settings/general',
      element: (
        <TenantRouteGuard minimumRole="owner">
          <GeneralSettingsPage />
        </TenantRouteGuard>
      ),
    },
    {
      path: 'tenants/:tenantId/settings/access',
      element: (
        <TenantRouteGuard minimumRole="owner">
          <AccessSettingsPage />
        </TenantRouteGuard>
      ),
    },
    {
      path: 'tenants/:tenantId/settings/enrollment',
      element: (
        <TenantRouteGuard minimumRole="administrator">
          <EnrollmentSettingsPage />
        </TenantRouteGuard>
      ),
    },
  ],
};
