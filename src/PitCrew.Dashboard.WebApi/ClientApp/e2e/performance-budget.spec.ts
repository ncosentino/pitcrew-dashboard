/**
 * Performance budgets and pagination/virtualization threshold evidence.
 *
 * Defines explicit thresholds per issue #91: initial route navigation
 * timing, bundle transfer budget, and the fleet-size threshold at which
 * pagination or virtualization is required.
 */
import { test, expect } from '@playwright/test';

import { healthyScenario, tenantId } from './mocks/scenarios';
import { buildFleetNode, buildFleetResponse, buildProfile } from './mocks/fixtures';
import { setUpPage } from './support/session';
import { measureNavigationTiming, recordMetric } from './support/metrics';
import { fleetPageSize } from '../src/features/fleet/FleetOverviewPage';

/**
 * Performance budget constants.
 *
 * VIRTUALIZATION_THRESHOLD: The fleet-node count at which the UI must
 * employ pagination or virtualization to avoid unbounded DOM growth.
 * Below this count, rendering all items inline is acceptable.
 */
const BUDGET = {
  /** Maximum ms for DOMContentLoaded on a typical fleet page. */
  domContentLoadedMs: 1500,
  /** Maximum ms for the load event on a typical fleet page. */
  loadEventMs: 3000,
  /** Maximum production JS transfer, with ~10% headroom over the 501,736-byte baseline. */
  bundleTransferBudgetBytes: 550_000,
  /** Node count at which pagination/virtualization is required. */
  virtualizationThreshold: fleetPageSize,
} as const;

test.describe('performance budgets', () => {
  test('typical fleet page navigation meets timing budget', async ({ page }, testInfo) => {
    await setUpPage(page, healthyScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('load');

    const timing = await measureNavigationTiming(page);

    expect(
      timing.domContentLoadedMs,
      `DOMContentLoaded (${timing.domContentLoadedMs}ms) within budget (${BUDGET.domContentLoadedMs}ms)`,
    ).toBeLessThanOrEqual(BUDGET.domContentLoadedMs);

    expect(
      timing.loadEventMs,
      `Load event (${timing.loadEventMs}ms) within budget (${BUDGET.loadEventMs}ms)`,
    ).toBeLessThanOrEqual(BUDGET.loadEventMs);

    await recordMetric({
      test: testInfo.title,
      route: `/tenants/${tenantId}/fleet`,
      viewport: 'desktop',
      theme: 'light',
      domContentLoadedMs: timing.domContentLoadedMs,
      loadEventMs: timing.loadEventMs,
    });
  });

  test('JS bundle transfer size within budget', async ({ page }) => {
    const requests: { url: string; size: number }[] = [];
    page.on('response', async (response) => {
      const url = response.url();
      if (url.includes('/assets/') && url.endsWith('.js')) {
        const headers = response.headers();
        const size = parseInt(headers['content-length'] || '0', 10);
        const body = await response.body().catch(() => Buffer.alloc(0));
        requests.push({ url, size: size || body.length });
      }
    });

    await setUpPage(page, healthyScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('load');

    const totalJs = requests.reduce((sum, r) => sum + r.size, 0);
    expect(
      totalJs,
      `Total JS transfer (${totalJs} bytes) within budget (${BUDGET.bundleTransferBudgetBytes} bytes)`,
    ).toBeLessThanOrEqual(BUDGET.bundleTransferBudgetBytes);
  });

  test(`fleet paginates above ${BUDGET.virtualizationThreshold} nodes`, async ({ page }) => {
    const nodes = Array.from({ length: BUDGET.virtualizationThreshold + 1 }, (_, i) => {
      const id = `00000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`;
      return buildFleetNode({
        nodeId: id,
        displayName: `Node-${i}`,
        isOnline: true,
        profiles: [buildProfile('build')],
      });
    });

    const scenario = {
      ...healthyScenario(),
      fleet: buildFleetResponse(nodes, []),
    };

    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');

    await expect(
      page.getByText(
        `Showing ${BUDGET.virtualizationThreshold} of ${BUDGET.virtualizationThreshold + 1} nodes`,
      ),
    ).toBeVisible();
    const tableRows = page
      .getByRole('region', { name: 'Fleet nodes for the active tenant' })
      .locator('[data-testid^="fleet-node-"]');
    await expect(tableRows).toHaveCount(BUDGET.virtualizationThreshold);

    await page.getByRole('button', { name: 'Show next 1' }).click();
    await expect(tableRows).toHaveCount(BUDGET.virtualizationThreshold + 1);
  });
});
