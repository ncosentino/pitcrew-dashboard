import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { SystemAdministratorGuard } from '@/core/routing/guards';

type CreateTenant = (
  tenantId: string,
  displayName: string,
  antiforgeryToken: string,
) => Promise<void>;

/** System administration feature routes. */
export function createAdminManifest(createTenant: CreateTenant): FeatureManifest {
  const TenantCreationPage = lazyFeature('admin', async () => {
    const module = await import('./TenantCreationPage');
    return { default: () => <module.default createTenant={createTenant} /> };
  });

  return {
    id: 'admin',
    navigation: [
      {
        label: 'Tenant administration',
        description: 'Create and manage tenant boundaries',
        path: '/admin/tenants',
        group: 'configure',
        order: 20,
        icon: 'tenants',
        requiresSystemAdministrator: true,
      },
    ],
    routePresentations: [
      {
        path: '/admin/tenants',
        title: 'Tenant administration',
        breadcrumbs: [{ label: 'Tenant administration' }],
      },
    ],
    routes: [
      {
        path: 'admin/tenants',
        element: (
          <SystemAdministratorGuard>
            <TenantCreationPage />
          </SystemAdministratorGuard>
        ),
      },
    ],
  };
}
