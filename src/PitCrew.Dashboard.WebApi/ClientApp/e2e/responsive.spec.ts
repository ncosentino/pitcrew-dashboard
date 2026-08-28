/**
 * Responsive foundation evidence (#87): validates compact summaries at narrow
 * viewports, comparison tables at desktop, keyboard-operable scroll regions,
 * touch targets, and zero overflow at 320/390/768/1280/1440 CSS px.
 */
import { test, expect } from '@playwright/test';

import { viewports } from '../playwright.config';
import { buildFleetNode, buildFleetResponse, buildProfile } from './mocks/fixtures';
import { activeIncidentScenario, healthyScenario, nodeIds, tenantId } from './mocks/scenarios';
import { setUpPage } from './support/session';
import { measureDocumentOverflow } from './support/overflow';

const scenario = healthyScenario();

function activeWorkloadScenario() {
  const profile = buildProfile('build');
  const source = profile.slots[0];
  const activeProfile = buildProfile('build', {
    managerContractVersion: 15,
    slots: [
      {
        ...source,
        activity: 'busy',
        runnerNameHash: 'a'.repeat(64),
        currentJob: {
          repository: 'https://github.com/example/project',
          workflowRunId: 12345,
          jobId: '67890',
          displayName: 'Compile and verify dashboard assets',
          eventName: 'push',
          queuedAt: '2026-07-19T18:20:00+00:00',
          scaleSetAssignedAt: '2026-07-19T18:21:00+00:00',
          runnerAssignedAt: '2026-07-19T18:22:00+00:00',
          startedAt: '2026-07-19T18:23:00+00:00',
          finishedAt: null,
          result: null,
        },
      },
    ],
  });
  const node = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: 'Alpha',
    isOnline: true,
    profiles: [activeProfile],
  });
  return {
    ...scenario,
    fleet: buildFleetResponse([node], []),
  };
}

test.describe('responsive mobile summaries', () => {
  const mobileViewport = viewports.mobile;
  const desktopViewport = viewports.desktop;

  test('fleet overview shows mobile summary cards at 390px', async ({ page }) => {
    await page.setViewportSize(mobileViewport);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await expect(page.locator('main')).toBeVisible();
    await page.waitForLoadState('networkidle');

    const mobileSummary = page.getByTestId('fleet-mobile-summary');
    await expect(mobileSummary).toBeVisible();

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'fleet overview at 390px').toBe(0);
  });

  test('fleet overview shows full table at 1440px', async ({ page }) => {
    await page.setViewportSize(desktopViewport);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await expect(page.locator('main')).toBeVisible();
    await page.waitForLoadState('networkidle');

    const mobileSummary = page.getByTestId('fleet-mobile-summary');
    await expect(mobileSummary).toBeHidden();

    const table = page.locator('table', { hasText: 'Fleet nodes for the active tenant' });
    await expect(table).toBeVisible();
  });

  test('incidents page prioritizes one case file and keeps its queue at 390px', async ({
    page,
  }) => {
    await page.setViewportSize(mobileViewport);
    await setUpPage(page, activeIncidentScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/incidents`);
    await expect(page.locator('main')).toBeVisible();
    await page.waitForLoadState('networkidle');

    await expect(
      page.getByRole('heading', { name: 'Sustained capacity deficit', level: 2 }),
    ).toBeVisible();
    await expect(page.getByText('Choose incident', { exact: true })).toBeVisible();
    await page.getByText('Choose incident', { exact: true }).click();
    await expect(page.getByRole('list', { name: 'Operational incident queue' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Open owning evidence' })).toBeVisible();

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'incidents at 390px').toBe(0);
  });

  test('runners page shows mobile cards at 390px', async ({ page }) => {
    await page.setViewportSize(mobileViewport);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/runners`);
    await expect(page.locator('main')).toBeVisible();
    await page.waitForLoadState('networkidle');

    const mobileSummary = page.getByTestId('runners-mobile-summary');
    await expect(mobileSummary).toBeVisible();

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'runners at 390px').toBe(0);
  });

  test('active jobs show prioritized mobile cards at 390px', async ({ page }) => {
    await page.setViewportSize(mobileViewport);
    await setUpPage(page, activeWorkloadScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}`);
    await page.waitForLoadState('networkidle');

    const pressureSection = page.getByTestId('node-overview-section-pressure');
    await expect(pressureSection).not.toHaveAttribute('open', '');
    await pressureSection.locator(':scope > summary').click();
    await expect(pressureSection).toHaveAttribute('open', '');
    await expect(page.getByTestId('active-workloads-mobile-summary')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Open in GitHub' })).toHaveCSS(
      'min-height',
      '44px',
    );
    expect((await measureDocumentOverflow(page)).overflowPx).toBe(0);
  });

  test('profile workers show prioritized mobile cards at 390px', async ({ page }) => {
    await page.setViewportSize(mobileViewport);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build/workers`);
    await page.waitForLoadState('networkidle');

    await expect(page.getByTestId('workers-mobile-summary')).toBeVisible();
    expect((await measureDocumentOverflow(page)).overflowPx).toBe(0);
  });
});

test.describe('zero overflow at all required widths', () => {
  const widths = [320, 390, 768, 1280, 1440] as const;
  const routes = [
    { name: 'fleet', path: `/tenants/${tenantId}/fleet` },
    { name: 'incidents', path: `/tenants/${tenantId}/incidents` },
    { name: 'runners', path: `/tenants/${tenantId}/runners` },
    {
      name: 'workers',
      path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build/workers`,
    },
    {
      name: 'node-profiles',
      path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles`,
    },
    { name: 'support', path: `/tenants/${tenantId}/support/sessions` },
  ];

  for (const route of routes) {
    for (const width of widths) {
      test(`${route.name} has no overflow at ${width}px`, async ({ page }) => {
        await page.setViewportSize({ width, height: 800 });
        await setUpPage(page, scenario, 'light');
        await page.goto(route.path);
        await expect(page.locator('main')).toBeVisible();
        await page.waitForLoadState('networkidle');

        const overflow = await measureDocumentOverflow(page);
        expect(overflow.overflowPx, `${route.name} at ${width}px`).toBe(0);
      });
    }
  }
});

test.describe('scrollable regions are keyboard-operable', () => {
  test('desktop table region is focusable via Tab', async ({ page }) => {
    await page.setViewportSize(viewports.desktop);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await expect(page.locator('main')).toBeVisible();
    await page.waitForLoadState('networkidle');

    const region = page.locator('[role="region"][aria-label="Fleet nodes for the active tenant"]');
    await expect(region).toBeVisible();
    await expect(region).toHaveAttribute('tabindex', '0');
  });
});

test.describe('touch targets meet minimum size', () => {
  test('mobile nav button meets 44px minimum', async ({ page }) => {
    await page.setViewportSize(viewports.mobile);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await expect(page.locator('main')).toBeVisible();

    const navButton = page.getByRole('button', { name: 'Open navigation' });
    const box = await navButton.boundingBox();
    expect(box).not.toBeNull();
    if (box === null) throw new Error('Open navigation button did not produce a bounding box.');
    expect(box.width).toBeGreaterThanOrEqual(44);
    expect(box.height).toBeGreaterThanOrEqual(44);
  });

  test('coarse pointers expand shared button targets to 44px', async ({ browser }) => {
    const context = await browser.newContext({
      hasTouch: true,
      isMobile: true,
      viewport: viewports.mobile,
    });
    const page = await context.newPage();
    try {
      await setUpPage(page, scenario, 'light');
      await page.goto(`/tenants/${tenantId}/fleet`);
      await page.getByRole('button', { name: 'Open navigation' }).click();

      const themeToggle = page.getByRole('button', { name: 'Use dark mode' });
      const box = await themeToggle.boundingBox();
      expect(box).not.toBeNull();
      if (box === null) throw new Error('Theme toggle did not produce a bounding box.');
      expect(box.width).toBeGreaterThanOrEqual(44);
      expect(box.height).toBeGreaterThanOrEqual(44);
    } finally {
      await context.close();
    }
  });
});

test.describe('reflow and expanded content', () => {
  test('fleet remains contained at a 200% zoom-equivalent width', async ({ page }) => {
    await page.setViewportSize({ width: 640, height: 900 });
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');

    await expect(page.getByTestId('fleet-mobile-summary')).toBeVisible();
    expect((await measureDocumentOverflow(page)).overflowPx).toBe(0);
  });

  test('runners contain long identifiers and 40% text expansion', async ({ page }, testInfo) => {
    await page.setViewportSize(viewports.mobile);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/runners`);
    await page.waitForLoadState('networkidle');

    const summary = page.getByTestId('runners-mobile-summary');
    await summary.evaluate((root) => {
      const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
      let node = walker.nextNode();
      while (node !== null) {
        const text = node.textContent?.trim() ?? '';
        if (text.length >= 4) {
          node.textContent = `${text} ${text.slice(0, Math.ceil(text.length * 0.4))}`;
        }
        node = walker.nextNode();
      }
      const identifier = root.querySelector('.font-mono');
      if (identifier) identifier.textContent = `profile-${'x'.repeat(160)}`;
    });

    expect((await measureDocumentOverflow(page)).overflowPx).toBe(0);
    await testInfo.attach('screenshot-runners-expanded-content', {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
  });
});
