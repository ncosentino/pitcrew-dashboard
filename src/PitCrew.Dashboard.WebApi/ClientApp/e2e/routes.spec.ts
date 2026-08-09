/**
 * Route-matrix coverage: representative Fleet/Incidents/Node/Profile/
 * Runners/Settings/login/error routes rendered at desktop light/dark, the
 * intermediate viewport, and strict 390px mobile. Each combination captures
 * a screenshot artifact, asserts no document-level horizontal overflow,
 * exactly one descriptive `<h1>`, a `<main>` landmark, and zero unexpected
 * serious/critical axe findings.
 *
 * Document overflow is asserted for real rather than assumed: it is measured
 * on every route/viewport combination, and only the specific combinations in
 * `KNOWN_BASELINE_OVERFLOW_ROUTES` below (an empirically observed, root-caused
 * ADR-0007 defect) are tolerated as baseline evidence instead of hard
 * failures. Every other combination still hard-fails on any nonzero reading.
 */
import { test, expect } from '@playwright/test';

import { viewports } from '../playwright.config';
import { healthyScenario, tenantId, nodeIds } from './mocks/scenarios';
import { setUpPage } from './support/session';
import { runAxeCheck } from './support/axe';
import { measureDocumentOverflow } from './support/overflow';
import {
  expectMainLandmark,
  expectSequentialHeadingOutline,
  expectSingleDescriptiveH1,
} from './support/landmarks';
import { measureNavigationTiming, recordMetric } from './support/metrics';

const scenario = healthyScenario();

interface RouteCase {
  readonly name: string;
  readonly path: string;
}

const routes: readonly RouteCase[] = [
  { name: 'fleet-overview', path: `/tenants/${tenantId}/fleet` },
  { name: 'incidents', path: `/tenants/${tenantId}/incidents` },
  { name: 'node-overview', path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}` },
  {
    name: 'node-administration',
    path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}/administration`,
  },
  {
    name: 'profile-overview',
    path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build`,
  },
  { name: 'runners', path: `/tenants/${tenantId}/runners` },
  { name: 'settings-general', path: `/tenants/${tenantId}/settings/general` },
  { name: 'settings-enrollment', path: `/tenants/${tenantId}/settings/enrollment` },
  { name: 'not-found', path: `/tenants/${tenantId}/this-route-does-not-exist` },
];

const themes = ['light', 'dark'] as const;
type ViewportName = keyof typeof viewports;
const viewportEntries = Object.entries(viewports) as ReadonlyArray<
  [ViewportName, (typeof viewports)[ViewportName]]
>;

// ADR-0007 baseline: Previously, `RunnersPage.tsx` and `IncidentsPage.tsx`
// each wrapped tables in raw `overflow-x-auto` containers that, as grid
// children with `min-width: auto`, pushed the document wider than the
// viewport. Issue #87 replaced those with ScrollableRegion (which applies
// `min-w-0 max-w-full overflow-x-auto`) and added mobile summary cards at
// narrow viewports, eliminating document-level overflow. The allowlist is
// now empty; all route/viewport combinations hard-fail on nonzero overflow.
const KNOWN_BASELINE_OVERFLOW_ROUTES: Readonly<Record<string, ReadonlySet<ViewportName>>> = {};

const REQUIRED_BASELINE_AXE_EVIDENCE: Readonly<Record<string, string>> = {};

for (const route of routes) {
  for (const theme of themes) {
    for (const [viewportName, viewportSize] of viewportEntries) {
      const caseName = `${route.name}--${theme}--${viewportName}`;

      test(`route matrix: ${caseName}`, async ({ page }, testInfo) => {
        await page.setViewportSize(viewportSize);
        await setUpPage(page, scenario, theme);
        await page.goto(route.path);
        await expect(page.locator('main')).toBeVisible();
        // Wait for the mocked network calls (which resolve near-instantly) to
        // go idle so overflow/axe/screenshot measurements read a
        // fully-painted DOM rather than a mid-render snapshot.
        await page.waitForLoadState('networkidle');

        await expectSingleDescriptiveH1(page);
        await expectSequentialHeadingOutline(page);
        await expectMainLandmark(page);

        const overflow = await measureDocumentOverflow(page);
        const isKnownBaselineOverflow =
          KNOWN_BASELINE_OVERFLOW_ROUTES[route.name]?.has(viewportName) ?? false;
        if (isKnownBaselineOverflow) {
          // Baseline evidence, not a silent skip: still asserts overflow is
          // present (proving the harness keeps measuring it) instead of
          // dropping the check entirely. See the allowlist comment above.
          expect(
            overflow.overflowPx,
            `expected known baseline overflow on ${caseName}`,
          ).toBeGreaterThan(0);
          await testInfo.attach(`baseline-overflow-${caseName}`, {
            body: JSON.stringify(overflow, null, 2),
            contentType: 'application/json',
          });
        } else {
          // No known ADR-0007 overflow defect has been observed on this route/
          // viewport combination; a nonzero reading here is a new regression, so
          // it hard-fails rather than being tolerated as baseline evidence.
          expect(overflow.overflowPx, `document overflow on ${caseName}`).toBe(0);
        }

        const axeResult = await runAxeCheck(page, testInfo, caseName);
        expect(
          axeResult.unexpected,
          `unexpected serious/critical axe violations on ${caseName}: ${axeResult.unexpected
            .map((violation) => violation.id)
            .join(', ')}`,
        ).toHaveLength(0);

        const expectedBaseline = REQUIRED_BASELINE_AXE_EVIDENCE[caseName];
        if (expectedBaseline !== undefined) {
          expect(
            axeResult.baseline.length,
            `expected ${expectedBaseline} baseline evidence on ${caseName}; remove its allowance if the defect was fixed`,
          ).toBeGreaterThan(0);
        }

        await testInfo.attach(`screenshot-${caseName}`, {
          body: await page.screenshot({ fullPage: true }),
          contentType: 'image/png',
        });

        const timing = await measureNavigationTiming(page);
        await recordMetric({
          test: caseName,
          route: route.path,
          viewport: viewportName,
          theme,
          ...timing,
        });
      });
    }
  }
}
