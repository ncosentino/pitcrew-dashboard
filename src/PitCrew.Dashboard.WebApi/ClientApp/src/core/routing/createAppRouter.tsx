import { createBrowserRouter, createMemoryRouter, type RouteObject } from 'react-router-dom';

import type { FeatureManifest } from '@/core/features/FeatureManifest';

import { AuthenticatedShell } from './AuthenticatedShell';
import { NoAccessPage, RootRedirect } from './pages';
import { SessionBoundary } from './SessionBoundary';

function createRoutes(features: ReadonlyArray<FeatureManifest>): RouteObject[] {
  const featureRoutes = features.flatMap((feature) => feature.routes);
  return [
    {
      element: <SessionBoundary />,
      children: [
        {
          path: '/',
          element: <AuthenticatedShell features={features} />,
          children: [
            { index: true, element: <RootRedirect /> },
            { path: 'no-access', element: <NoAccessPage /> },
            ...featureRoutes,
            { path: '*', element: <NoAccessPage /> },
          ],
        },
      ],
    },
  ];
}

/** Creates the production browser router from registered feature manifests. */
export function createAppRouter(features: ReadonlyArray<FeatureManifest>) {
  return createBrowserRouter(createRoutes(features));
}

/** Creates an in-memory router over the same production route graph. */
export function createTestRouter(
  features: ReadonlyArray<FeatureManifest>,
  initialEntries: string[],
) {
  return createMemoryRouter(createRoutes(features), { initialEntries });
}
