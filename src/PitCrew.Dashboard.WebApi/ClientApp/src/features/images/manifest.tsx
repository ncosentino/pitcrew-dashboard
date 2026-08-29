import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { lazyFeature } from '@/core/features/lazyFeature';
import { TenantRouteGuard } from '@/core/routing/guards';

const ImageWorkspace = lazyFeature('images', () => import('./ImageWorkspace'));
const ImageWorkspaceLandingPage = lazyFeature('images', async () => {
  const module = await import('./ImageWorkspace');
  return { default: module.ImageWorkspaceLandingPage };
});
const ImageCandidatesPage = lazyFeature('images', () => import('./ImageCandidatesPage'));
const ImageRecipesPage = lazyFeature('images', () => import('./ImageRecipesPage'));

/** Trusted runner-image candidate routes and navigation contribution. */
export const imagesManifest: FeatureManifest = {
  id: 'images',
  navigation: [
    {
      label: 'Runner images',
      description: 'Candidate builds and qualification evidence',
      path: '/tenants/:tenantId/images',
      group: 'operate',
      order: 20,
      icon: 'images',
      activePathPatterns: ['/tenants/:tenantId/images', '/tenants/:tenantId/images/*'],
    },
  ],
  routePresentations: [
    {
      path: '/tenants/:tenantId/images',
      title: 'Runner images',
      breadcrumbs: [{ label: 'Runner images' }],
    },
    {
      path: '/tenants/:tenantId/images/candidates',
      title: 'Runner image candidates',
      breadcrumbs: [
        { label: 'Runner images', path: '/tenants/:tenantId/images' },
        { label: 'Candidates' },
      ],
    },
    {
      path: '/tenants/:tenantId/images/recipes',
      title: 'Trusted image recipes',
      breadcrumbs: [
        { label: 'Runner images', path: '/tenants/:tenantId/images' },
        { label: 'Recipes' },
      ],
    },
  ],
  routes: [
    {
      path: 'tenants/:tenantId/images',
      element: (
        <TenantRouteGuard minimumRole="viewer">
          <ImageWorkspace />
        </TenantRouteGuard>
      ),
      children: [
        { index: true, element: <ImageWorkspaceLandingPage /> },
        { path: 'candidates', element: <ImageCandidatesPage /> },
        { path: 'recipes', element: <ImageRecipesPage /> },
      ],
    },
  ],
};
