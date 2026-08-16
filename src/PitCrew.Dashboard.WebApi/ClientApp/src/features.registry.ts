import type { FeatureManifest } from '@/core/features/FeatureManifest';
import { createAdminManifest } from '@/features/admin/manifest';
import { fleetManifest } from '@/features/fleet/manifest';
import { runnersManifest } from '@/features/runners/manifest';
import { settingsManifest } from '@/features/settings/manifest';
import { supportManifest } from '@/features/support/manifest';
import { createTenant } from '@/features/settings/settingsApi';

/** Complete compile-time feature registry for the dashboard. */
export const features: ReadonlyArray<FeatureManifest> = [
  fleetManifest,
  runnersManifest,
  settingsManifest,
  supportManifest,
  createAdminManifest(createTenant),
];
