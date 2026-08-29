import type { RouteObject } from 'react-router-dom';

/** Operator-intent groups supported by the authenticated shell. */
export type FeatureNavigationGroup = 'monitor' | 'operate' | 'configure';

/** Shell-owned icon vocabulary available to feature navigation contributions. */
export type FeatureNavigationIcon =
  'fleet' | 'incidents' | 'images' | 'runners' | 'support' | 'settings' | 'tenants';

/** Navigation contributed by one independently owned feature. */
export interface FeatureNavigationItem {
  readonly label: string;
  readonly description: string;
  readonly path: string;
  readonly group: FeatureNavigationGroup;
  readonly order: number;
  readonly icon: FeatureNavigationIcon;
  readonly minimumTenantRole?: 'viewer' | 'administrator' | 'owner';
  readonly requiresSystemAdministrator?: boolean;
  readonly activePathPatterns?: ReadonlyArray<string>;
}

/** One link or current-location label in a route breadcrumb trail. */
export interface FeatureBreadcrumbItem {
  readonly label: string;
  readonly path?: string;
}

/** Shell presentation contributed for one feature-owned route. */
export interface FeatureRoutePresentation {
  readonly path: string;
  readonly title: string;
  readonly breadcrumbs: ReadonlyArray<FeatureBreadcrumbItem>;
}

/** Routes and navigation contributed by one feature plugin. */
export interface FeatureManifest {
  readonly id: string;
  readonly routes: ReadonlyArray<RouteObject>;
  readonly navigation?: ReadonlyArray<FeatureNavigationItem>;
  readonly routePresentations?: ReadonlyArray<FeatureRoutePresentation>;
}
