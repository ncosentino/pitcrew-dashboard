/**
 * Named-state coverage: healthy, active incident, offline/stale,
 * unavailable, empty, permission-limited, and failed-mutation — the seven
 * states issue #84 requires representative evidence for. Each test renders
 * the state-appropriate page and asserts on real, already-shipped UI text
 * (not synthetic test-only markers), keeping production components free of
 * test-only props.
 */
import { test, expect, type Page, type TestInfo } from '@playwright/test';

import {
  healthyScenario,
  activeIncidentScenario,
  offlineStaleScenario,
  unavailableScenario,
  emptyScenario,
  permissionLimitedScenario,
  failedMutationScenario,
  tenantId,
} from './mocks/scenarios';
import { setUpPage } from './support/session';
import { runAxeCheck } from './support/axe';
import {
  expectMainLandmark,
  expectSequentialHeadingOutline,
  expectSingleDescriptiveH1,
} from './support/landmarks';
import { measureDocumentOverflow } from './support/overflow';

const fleetPath = `/tenants/${tenantId}/fleet`;

async function expectStateEvidence(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  await page.waitForLoadState('networkidle');
  await expectSingleDescriptiveH1(page);
  await expectSequentialHeadingOutline(page);
  await expectMainLandmark(page);

  const overflow = await measureDocumentOverflow(page);
  expect(overflow.overflowPx, `document overflow on state ${name}`).toBe(0);

  const axeResult = await runAxeCheck(page, testInfo, `state-${name}`);
  expect(
    axeResult.unexpected,
    `unexpected serious/critical axe violations on state ${name}: ${axeResult.unexpected
      .map((violation) => violation.id)
      .join(', ')}`,
  ).toHaveLength(0);

  await testInfo.attach(`screenshot-state-${name}`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
}

test('healthy: fleet renders with no active-incident banner', async ({ page }, testInfo) => {
  await setUpPage(page, healthyScenario(), 'light');
  await page.goto(fleetPath);

  await expect(page.getByRole('heading', { name: 'Fleet status' })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);

  await expectStateEvidence(page, testInfo, 'healthy');
});

test('active incident: an active incident is surfaced on the incidents page', async ({
  page,
}, testInfo) => {
  await setUpPage(page, activeIncidentScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/incidents`);

  await expect(page.getByText('Sustained capacity deficit')).toBeVisible();
  await expectStateEvidence(page, testInfo, 'active-incident');
});

test('offline/stale: an offline node with stale hardware is visible', async ({
  page,
}, testInfo) => {
  await setUpPage(page, offlineStaleScenario(), 'light');
  await page.goto(fleetPath);

  await expect(page.getByText('Bravo')).toBeVisible();
  await expect(page.getByText('Retained cause: synchronization-network')).toBeVisible();
  await expectStateEvidence(page, testInfo, 'offline-stale');
});

test('unavailable: fleet snapshot network failure renders an alert', async ({ page }, testInfo) => {
  await setUpPage(page, unavailableScenario(), 'light');
  await page.goto(fleetPath);

  await expect(page.getByRole('alert')).toBeVisible();
  await expectStateEvidence(page, testInfo, 'unavailable');
});

test('empty: a tenant with no enrolled nodes shows the enrollment prompt', async ({
  page,
}, testInfo) => {
  await setUpPage(page, emptyScenario(), 'light');
  await page.goto(fleetPath);

  await expect(page.getByRole('heading', { level: 3, name: 'No servers enrolled' })).toBeVisible();
  await expectStateEvidence(page, testInfo, 'empty');
});

test('permission-limited: a viewer session cannot reach an owner-only settings route', async ({
  page,
}, testInfo) => {
  await setUpPage(page, permissionLimitedScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/settings/general`);

  await expect(page.getByText('Insufficient tenant role')).toBeVisible();
  await expectStateEvidence(page, testInfo, 'permission-limited');
});

test('failed-mutation: enrollment code creation surfaces a role="alert" error', async ({
  page,
}, testInfo) => {
  await setUpPage(page, failedMutationScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/settings/enrollment`);

  await page.getByRole('button', { name: 'Create one-time code' }).click();
  const alert = page.getByRole('alert');
  await expect(alert).toBeVisible();
  await expect(alert).toHaveText('The request could not be completed. Try again.');
  await expectStateEvidence(page, testInfo, 'failed-mutation');
});
