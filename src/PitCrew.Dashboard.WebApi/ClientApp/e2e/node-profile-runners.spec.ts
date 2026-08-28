import { test, expect, type Page, type TestInfo } from '@playwright/test';

import { viewports } from '../playwright.config';
import { buildFleetNode, buildFleetResponse, buildProfile } from './mocks/fixtures';
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

test('desktop node profiles switch between list and a persisted comparison table', async ({
  page,
}, testInfo) => {
  await page.setViewportSize(viewports.wide);
  await setUpPage(page, healthyScenario(), 'dark');
  await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles`);

  await expect(page.getByTestId('node-profile-build')).toBeVisible();
  await expect(page.getByTestId('node-profile-deploy')).toBeVisible();

  await page.getByRole('button', { name: 'Table' }).click();

  const table = page.getByTestId('node-profile-comparison-table');
  await expect(table).toBeVisible();
  await expect(table.getByRole('columnheader')).toHaveText([
    'Profile',
    'State',
    'Configured',
    'Desired',
    'Local',
    'Eligible',
    'Draining',
    'Resources',
    'Evidence',
  ]);
  await expect(table.getByTestId('node-profile-table-build')).toContainText('Build');
  await expect(table.getByTestId('node-profile-table-deploy')).toContainText('Deploy');

  await page.reload();
  await expect(page.getByTestId('node-profile-comparison-table')).toBeVisible();

  await expectSurfaceHealth(page, testInfo, 'node-profile-comparison-table');
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

test('runner dispatch sheet leads with explicit current-job correlation', async ({
  page,
}, testInfo) => {
  await page.setViewportSize(viewports.desktop);
  await setUpPage(page, activeJobScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/runners`);

  await expect(
    page.getByRole('heading', { level: 2, name: 'Compile and verify dashboard assets' }),
  ).toBeVisible();
  await expect(page.getByRole('region', { name: 'Current GitHub job' })).toContainText(
    'In progress',
  );
  await expect(page.getByRole('link', { name: 'Open job in GitHub' })).toHaveAttribute(
    'href',
    'https://github.com/example/project/actions/runs/12345/job/67890',
  );
  await expect(
    page.getByRole('region', { name: 'Runner readiness' }).getByText('Current jobs').locator('..'),
  ).toContainText('1');

  await expectSurfaceHealth(page, testInfo, 'runner-current-job-desktop');
  await testInfo.attach('screenshot-runner-current-job-desktop', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});

test('offline runner correlation remains explicitly last-known', async ({ page }, testInfo) => {
  await page.setViewportSize(viewports.desktop);
  const scenario = activeJobScenario();
  const offlineScenario = {
    ...scenario,
    fleet: {
      ...scenario.fleet,
      nodes: scenario.fleet.nodes.map((node) => ({ ...node, isOnline: false })),
    },
  };
  await setUpPage(page, offlineScenario, 'light');
  await page.goto(`/tenants/${tenantId}/runners`);

  await expect(
    page.getByRole('heading', {
      level: 2,
      name: 'Last known: Compile and verify dashboard assets',
    }),
  ).toBeVisible();
  await expect(page.getByRole('region', { name: 'Last-known GitHub job evidence' })).toContainText(
    'Last reported in progress',
  );
  await expect(page.getByText(/because the node is offline/)).toBeVisible();
  await expect(
    page.getByRole('region', { name: 'Runner readiness' }).getByText('Current jobs').locator('..'),
  ).toContainText('0');

  await expectSurfaceHealth(page, testInfo, 'runner-last-known-job-desktop');
  await testInfo.attach('screenshot-runner-last-known-job-desktop', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});

test('mobile runner selection focuses an explicit no-job dispatch sheet', async ({
  page,
}, testInfo) => {
  await page.setViewportSize(viewports.mobile);
  await setUpPage(page, activeJobScenario(), 'dark');
  await page.goto(`/tenants/${tenantId}/runners`);

  const idleCard = page.getByTestId(`runner-card-${nodeIds.alpha}-build-build-000002`);
  await idleCard.getByRole('link', { name: 'Investigate' }).click();

  const selected = page.getByRole('region', { name: 'Selected runner investigation' });
  await expect(selected).toBeVisible();
  await expect(page.getByRole('heading', { level: 2, name: 'Alpha · build-000002' })).toBeFocused();
  await expect(page.getByText('No current GitHub job is assigned to this runner.')).toBeVisible();
  await expect(idleCard.getByRole('link', { name: 'Selected' })).toHaveAttribute(
    'aria-current',
    'page',
  );

  await expectSurfaceHealth(page, testInfo, 'runner-selection-mobile');
  await testInfo.attach('screenshot-runner-selection-mobile', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});

test('runner job and identity evidence remains contained at 320px', async ({ page }, testInfo) => {
  const profileId = `profile-${'p'.repeat(120)}`;
  const baseline = buildProfile(profileId);
  const slotKey = `slot-${'s'.repeat(120)}`;
  const repository = `https://github.com/example/${'r'.repeat(100)}`;
  const profile = buildProfile(profileId, {
    managerContractVersion: 15,
    desiredSlots: 1,
    configuredSlots: 1,
    activeSlots: 1,
    eligibleSlots: 1,
    drainingSlots: 0,
    capacityEvidence: {
      fixed: {
        observedAt: '2026-07-19T18:30:00+00:00',
        freshness: 'current',
        targetSlots: 1,
        activeWorkers: 1,
        startingWorkers: 0,
        drainingWorkers: 0,
        cleanupPendingWorkers: 0,
        eligibleWorkers: 1,
        localDeficit: 0,
        eligibilityDeficit: 0,
        reason: 'none',
        evidence: null,
      },
      targets: [],
    },
    update: {
      status: 'current',
      targetImage: null,
      targetImageId: null,
      targetRevision: 'b'.repeat(64),
      currentWorkers: 1,
      staleWorkers: 0,
      lastError: null,
    },
    slots: [
      {
        ...baseline.slots[0],
        key: slotKey,
        repository,
        activity: 'busy',
        runnerNameHash: 'b'.repeat(64),
        currentJob: {
          repository,
          workflowRunId: 98765,
          jobId: '43210',
          displayName: `Job-${'j'.repeat(240)}`,
          eventName: 'workflow_dispatch',
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
    displayName: `Node-${'n'.repeat(150)}`,
    isOnline: true,
    profiles: [profile],
  });
  const scenario = {
    ...healthyScenario(),
    fleet: buildFleetResponse([node], []),
  };

  await page.setViewportSize(viewports.narrow);
  await setUpPage(page, scenario, 'light');
  await page.goto(`/tenants/${tenantId}/runners`);

  await expect(page.getByRole('region', { name: 'Selected runner investigation' })).toBeVisible();
  await expect(page.getByTestId(/^runner-card-/)).toBeVisible();
  await expectSurfaceHealth(page, testInfo, 'runner-long-content-narrow');
  await testInfo.attach('screenshot-runner-long-content-narrow', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});

test('mobile node overview exposes a compact section index before detailed evidence', async ({
  page,
}) => {
  await page.setViewportSize(viewports.mobile);
  await setUpPage(page, pressureScenario(), 'dark');
  await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}`);

  await expect(page.getByRole('region', { name: 'Node readiness' })).toContainText('1');
  await expect(page.getByRole('link', { name: /Profiles/ })).toBeVisible();

  const sectionIds = [
    'node-overview-section-identity',
    'node-overview-section-pressure',
    'node-overview-section-connector',
    'node-overview-section-hardware',
  ] as const;
  for (const testId of sectionIds) {
    const section = page.getByTestId(testId);
    await expect(section.locator(':scope > summary')).toBeVisible();
    await expect(section).not.toHaveAttribute('open', '');
  }

  const firstSummary = await page
    .getByTestId(sectionIds[0])
    .locator(':scope > summary')
    .boundingBox();
  const lastSummary = await page
    .getByTestId(sectionIds[3])
    .locator(':scope > summary')
    .boundingBox();
  expect(firstSummary).not.toBeNull();
  expect(lastSummary).not.toBeNull();
  expect((lastSummary?.y ?? 0) - (firstSummary?.y ?? 0)).toBeLessThan(320);

  const pressure = page.getByTestId('node-overview-section-pressure');
  await pressure.locator(':scope > summary').click();
  await expect(
    page.getByRole('heading', { level: 3, name: 'Host pressure and active workloads' }),
  ).toBeVisible();
  await pressure.locator(':scope > summary').click();

  await page.getByRole('link', { name: /Profiles/ }).click();
  const profile = page.getByTestId('node-profile-build');
  await expect(
    profile.getByRole('link', { name: /Review (overview|capacity|workers|diagnostics)/ }),
  ).toBeVisible();
  await expect(profile.getByText('Configured')).toBeHidden();
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
    name: 'profile-inventory',
    scenario: healthyScenario,
    path: `/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles`,
    readyText: 'Profiles requiring attention',
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

        if (
          viewport.name === 'mobile' &&
          state.path === `/tenants/${tenantId}/nodes/${nodeIds.alpha}` &&
          'readyText' in state
        ) {
          await expect(page.getByRole('heading', { level: 2, name: 'Alpha' })).toBeVisible();
        } else if ('readyHeading' in state) {
          await expect(
            page.getByRole('heading', { level: 2, name: state.readyHeading }),
          ).toBeVisible();
        } else {
          await expect(page.getByText(state.readyText).last()).toBeVisible();
        }
        await expectSurfaceHealth(page, testInfo, `${state.name}-${theme}-${viewport.name}`);
      });
    }
  }
}
