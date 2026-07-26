import type { RouteObject } from 'react-router-dom';

/** Navigation contributed by one independently owned feature. */
export interface FeatureNavigationItem {
  readonly label: string;
  readonly path: string;
  readonly minimumTenantRole?: 'viewer' | 'administrator' | 'owner';
}

/** Routes and navigation contributed by one feature plugin. */
export interface FeatureManifest {
  readonly id: string;
  readonly routes: ReadonlyArray<RouteObject>;
  readonly navigation?: ReadonlyArray<FeatureNavigationItem>;
}
