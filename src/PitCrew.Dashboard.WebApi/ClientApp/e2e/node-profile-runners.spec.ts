import { test, expect, type Page, type TestInfo } from '@playwright/test';

import { viewports } from '../playwright.config';
import {
  activeJobScenario,
  degradedNodeScenario,
  healthyScenario,
  pressureScenario,
  readOnlyScenario,
  recoveryScenario,
  rollingImageScenario,
  tenantId,
  unavailableScenario,
  nodeIds,
} from './mocks/scenarios';
import { runAxeCheck } from './support/axe';
import { expectMainLandmark, expectSequentialHeadingOutline } from './support/landmarks';
import { measureDocumentOverflow } from './support/overflow';
import { setUpPage } from './support/session';

async function expectSurfaceHealth(
  page: Page,
  testInfo: TestInfo,
  artifactName: string,
): Promise<void> {
  await page.waitForLoadState('networkidle');
  await expectMainLandmark(page);
  await expectSequentialHeadingOutline(page);

  const overflow = await measureDocumentOverflow(page);
  expect(overflow.overflowPx, `document overflow on ${artifactName}`).toBe(0);

  const axeResult = await runAxeCheck(page, testInfo, artifactName);
  expect(
    axeResult.unexpected,
    `unexpected serious/critical axe violations on ${artifactName}: ${axeResult.unexpected
      .map((violation) => violation.id)
      .join(', ')}`,
  ).toHaveLength(0);
}

test('node detail renders an EntityHeader with the node display name title', async ({ page }) => {
  await setUpPage(page, healthyScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}`);

  await expect(page.getByRole('heading', { level: 2, name: 'Alpha' })).toBeVisible();
  await expect(page.getByRole('button', { name: `Copy Alpha node ID` })).toBeVisible();
});

test('profile detail renders an EntityHeader for the profile', async ({ page }) => {
  await setUpPage(page, healthyScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build`);

  await expect(page.getByRole('heading', { level: 2, name: 'Build' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Copy build profile ID' })).toBeVisible();
});

test('runners page shows filter chips and result count', async ({ page }) => {
  await setUpPage(page, healthyScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/runners`);

  await page.getByLabel('Node').selectOption(nodeIds.alpha);
  await page.getByLabel('Repository').fill('example/project');

  await expect(page.getByText('Showing 2 of 6 slots')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Remove filter Node: Alpha' })).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'Remove filter Repository: example/project' }),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Clear all filters' })).toBeVisible();
});

test('advanced runners filters disclosure opens and exposes sorting controls', async ({ page }) => {
  await setUpPage(page, healthyScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/runners`);

  const disclosure = page.locator('details').first();
  await expect(disclosure).toHaveJSProperty('open', false);

  await page.locator('summary', { hasText: 'Advanced filters and sorting' }).click();
  await expect(disclosure).toHaveJSProperty('open', true);
  await expect(page.getByLabel('Sort by')).toBeVisible();
  await expect(page.getByLabel('Sort direction')).toBeVisible();
});

test('runners table prioritizes operator decisions over raw telemetry columns', async ({
  page,
}) => {
  await page.setViewportSize(viewports.wide);
  await setUpPage(page, healthyScenario(), 'dark');
  await page.goto(`/tenants/${tenantId}/runners`);

  const table = page.getByRole('table', { name: 'Runner slots for the active tenant' });
  await expect(table.getByRole('columnheader')).toHaveText([
    'Runner',
    'Workload',
    'State',
    'Resources',
    'Evidence',
  ]);
  await expect(table.getByRole('columnheader', { name: 'Network I/O' })).toHaveCount(0);
  await expect(table.getByRole('columnheader', { name: 'Worker image' })).toHaveCount(0);

  const firstRow = table.getByRole('row').nth(2);
  await firstRow.getByText('Technical details').click();
  await expect(firstRow.getByText('Network')).toBeVisible();
  await expect(firstRow.getByText('Block I/O')).toBeVisible();
  await expect(firstRow.getByText('Image')).toBeVisible();
  await expect(firstRow.getByText('Last exit')).toBeVisible();
});

test('mobile runner summaries retain technical evidence behind disclosure', async ({ page }) => {
  await page.setViewportSize(viewports.mobile);
  await setUpPage(page, healthyScenario(), 'dark');
  await page.goto(`/tenants/${tenantId}/runners`);

  const card = page.getByTestId(/^runner-card-/).first();
  await card.getByText('Technical details').click();
  await expect(card.getByText('Network')).toBeVisible();
  await expect(card.getByText('Block I/O')).toBeVisible();
  await expect(card.getByText('Image')).toBeVisible();
  await expect(card.getByText('Last exit')).toBeVisible();
  await expect(card.getByText('Updated')).toBeVisible();
});

const matrix = [
  {
    name: 'healthy',
    scenario: healthyScenario,
    path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}`,
    readyHeading: 'Alpha',
  },
  {
    name: 'degraded',
    scenario: degradedNodeScenario,
    path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}`,
    readyText: 'Connector outage evidence',
  },
  {
    name: 'pressure',
    scenario: pressureScenario,
    path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}`,
    readyText: 'Host pressure and active workloads',
  },
  {
    name: 'active-job',
    scenario: activeJobScenario,
    path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}`,
    readyText: 'Active workers and jobs',
  },
  {
    name: 'rolling-image',
    scenario: rollingImageScenario,
    path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build/workers`,
    readyText: 'Worker image rollout',
  },
  {
    name: 'recovery',
    scenario: recoveryScenario,
    path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build/recovery`,
    readyText: 'Manager recovery',
  },
  {
    name: 'read-only',
    scenario: readOnlyScenario,
    path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build`,
    readyHeading: 'Build',
  },
  {
    name: 'unavailable',
    scenario: unavailableScenario,
    path: `/tenants/${tenantId}/runners`,
    readyText: 'Runner data is unavailable',
  },
] as const;

const themeMatrix = ['light', 'dark'] as const;
const viewportMatrix = [
  { name: 'desktop', size: viewports.desktop },
  { name: 'mobile', size: viewports.mobile },
] as const;

for (const state of matrix) {
  for (const theme of themeMatrix) {
    for (const viewport of viewportMatrix) {
      test(`${state.name} renders without overflow or axe violations in ${theme} ${viewport.name}`, async ({
        page,
      }, testInfo) => {
        await page.setViewportSize(viewport.size);
        await setUpPage(page, state.scenario(), theme);
        await page.goto(state.path);

        if ('readyHeading' in state) {
          await expect(
            page.getByRole('heading', { level: 2, name: state.readyHeading }),
          ).toBeVisible();
        } else {
          await expect(page.getByText(state.readyText).first()).toBeVisible();
        }
        await expectSurfaceHealth(page, testInfo, `${state.name}-${theme}-${viewport.name}`);
      });
    }
  }
}
