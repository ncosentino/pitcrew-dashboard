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
    },
    {
      label: 'Enrollment',
      path: '/tenants/:tenantId/settings/enrollment',
      minimumTenantRole: 'administrator',
    },
  ],
  routes: [
    {
      element: <TenantRouteGuard minimumRole="owner" />,
      children: [
        { path: 'tenants/:tenantId/settings/general', element: <GeneralSettingsPage /> },
        { path: 'tenants/:tenantId/settings/access', element: <AccessSettingsPage /> },
      ],
    },
    {
      element: <TenantRouteGuard minimumRole="administrator" />,
      children: [
        { path: 'tenants/:tenantId/settings/enrollment', element: <EnrollmentSettingsPage /> },
      ],
    },
  ],
};
