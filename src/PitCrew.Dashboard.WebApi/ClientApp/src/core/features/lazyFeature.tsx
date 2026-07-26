import { lazy, Suspense, type ComponentType } from 'react';

import { FeatureErrorBoundary } from './FeatureErrorBoundary';

type FeatureModule = Readonly<{ default: ComponentType }>;

/** Creates a lazy feature entrypoint with consistent loading and failure boundaries. */
export function lazyFeature(
  featureId: string,
  loader: () => Promise<FeatureModule>,
): ComponentType {
  const LazyComponent = lazy(loader);

  return function LazyFeature() {
    return (
      <FeatureErrorBoundary featureId={featureId}>
        <Suspense fallback={<p className="text-muted-foreground">Loading feature…</p>}>
          <LazyComponent />
        </Suspense>
      </FeatureErrorBoundary>
    );
  };
}
