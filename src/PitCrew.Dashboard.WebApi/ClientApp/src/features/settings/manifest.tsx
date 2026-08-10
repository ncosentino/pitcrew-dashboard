import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const GeneralSettingsPage = lazyFeature('settings', async () => {
  const module = await import('./SettingsPages');
  return { default: module.GeneralSettingsPage };
});
const SettingsLandingPage = lazyFeature('settings', async () => {
  const module = await import('./SettingsPages');
  return { default: module.SettingsLandingPage };
});
const AccessSettingsPage = lazyFeature('settings', async () => {
  const module = await import('./SettingsPages');
  return { default: module.AccessSettingsPage };
});
const EnrollmentSettingsPage = lazyFeature('settings', async () => {
  const module = await import('./SettingsPages');
  return { default: module.EnrollmentSettingsPage };
});
const DiagnosticsSettingsPage = lazyFeature('settings', async () => {
  const module = await import('./SettingsPages');
  return { default: module.DiagnosticsSettingsPage };
});

/** Settings feature routes and navigation contribution. */
export const settingsManifest: FeatureManifest = {
  id: 'settings',
  navigation: [
    {
      label: 'Settings',
      path: '/tenants/:tenantId/settings',
      minimumTenantRole: 'administrator',
      activePathPatterns: [
        '/tenants/:tenantId/settings/general',
        '/tenants/:tenantId/settings/access',
        '/tenants/:tenantId/settings/enrollment',
        '/tenants/:tenantId/settings/diagnostics',
      ],
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
        { label: 'Settings', path: '/tenants/:tenantId/settings' },
        { label: 'Access' },
      ],
    },
    {
      path: '/tenants/:tenantId/settings/enrollment',
      title: 'Connector enrollment',
      breadcrumbs: [
        { label: 'Settings', path: '/tenants/:tenantId/settings' },
        { label: 'Enrollment' },
      ],
    },
    {
      path: '/tenants/:tenantId/settings/diagnostics',
      title: 'Diagnostic credentials',
      breadcrumbs: [
        { label: 'Settings', path: '/tenants/:tenantId/settings' },
        { label: 'Diagnostics' },
      ],
    },
  ],
  routes: [
    {
      path: 'tenants/:tenantId/settings',
      element: (
        <TenantRouteGuard minimumRole="administrator">
          <SettingsLandingPage />
        </TenantRouteGuard>
      ),
    },
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
    {
      path: 'tenants/:tenantId/settings/diagnostics',
      element: (
        <TenantRouteGuard minimumRole="administrator">
          <DiagnosticsSettingsPage />
        </TenantRouteGuard>
      ),
    },
  ],
};
