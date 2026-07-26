import { matchPath, type Params } from 'react-router-dom';

import type { FeatureManifest, FeatureRoutePresentation } from '@/core/features/FeatureManifest';

export interface MatchedRoutePresentation {
  readonly presentation: FeatureRoutePresentation;
  readonly params: Params<string>;
}

const noAccessPresentation: FeatureRoutePresentation = {
  path: '/no-access',
  title: 'No tenant access',
  breadcrumbs: [{ label: 'No tenant access' }],
};

const notFoundPresentation: FeatureRoutePresentation = {
  path: '*',
  title: 'Page not found',
  breadcrumbs: [{ label: 'Page not found' }],
};

/** Replaces route placeholders with the parameters from the active URL. */
export function formatRouteLabel(label: string, params: Params<string>): string {
  return label.replaceAll(/:([A-Za-z0-9_]+)/g, (token, key: string) => params[key] ?? token);
}

/** Resolves route presentation without importing a feature into the shell. */
export function matchRoutePresentation(
  features: ReadonlyArray<FeatureManifest>,
  pathname: string,
): MatchedRoutePresentation {
  const presentations = features.flatMap((feature) => feature.routePresentations ?? []);
  if (pathname === noAccessPresentation.path) {
    return { presentation: noAccessPresentation, params: {} };
  }

  for (const presentation of presentations) {
    const match = matchPath({ path: presentation.path, end: true }, pathname);
    if (match) return { presentation, params: match.params };
  }

  return { presentation: notFoundPresentation, params: {} };
}
