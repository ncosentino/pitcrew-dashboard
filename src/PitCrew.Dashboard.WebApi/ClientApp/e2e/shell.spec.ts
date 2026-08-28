import { test, expect } from '@playwright/test';

import { viewports } from '../playwright.config';
import { shellRailStorageKey } from '../src/core/routing/AuthenticatedShell';
import { buildSession, buildTenantAccess } from './mocks/fixtures';
import { activeIncidentScenario, tenantId } from './mocks/scenarios';
import { runAxeCheck } from './support/axe';
import { measureDocumentOverflow } from './support/overflow';
import { setUpPage } from './support/session';

const longTenantName =
  'Local build and release operations with extended infrastructure coordination';

function shellScenario() {
  const scenario = activeIncidentScenario();
  return {
    ...scenario,
    session: buildSession('owner', {
      isSystemAdministrator: true,
      tenants: [buildTenantAccess('owner', { displayName: longTenantName })],
    }),
  };
}

test('desktop shell groups work and remembers the compact rail', async ({ page }, testInfo) => {
  await page.setViewportSize(viewports.desktop);
  await setUpPage(page, shellScenario(), 'light');
  await page.goto(`/tenants/${tenantId}/fleet`);

  const navigation = page.getByRole('navigation', { name: 'Primary navigation' });
  await expect(navigation.getByRole('list', { name: 'Monitor' })).toBeVisible();
  await expect(navigation.getByRole('list', { name: 'Operate' })).toBeVisible();
  await expect(navigation.getByRole('list', { name: 'Configure' })).toBeVisible();
  await expect(navigation.getByRole('link', { name: 'Incidents' })).toHaveAccessibleDescription(
    /1 active incident; highest severity critical/,
  );

  const collapse = page.getByRole('button', { name: 'Collapse primary navigation' });
  await collapse.focus();
  await collapse.press('Enter');

  await expect(page.getByRole('button', { name: 'Expand primary navigation' })).toBeVisible();
  await expect(navigation).toHaveAttribute('data-rail-mode', 'compact');
  await expect(navigation.getByRole('link', { name: 'Fleet' })).toBeVisible();
  await expect(navigation.getByRole('link', { name: 'Fleet' })).toHaveAccessibleDescription(
    'Readiness, nodes, and profile health',
  );
  await expect(navigation.getByRole('link', { name: 'Incidents' })).toHaveAccessibleDescription(
    /1 active incident; highest severity critical/,
  );
  await expect(navigation.getByRole('link', { name: 'Tenant administration' })).toBeVisible();
  expect(await page.evaluate((key) => localStorage.getItem(key), shellRailStorageKey)).toBe(
    'compact',
  );
  expect((await measureDocumentOverflow(page)).overflowPx).toBe(0);

  await page.reload();
  await expect(page.getByRole('button', { name: 'Expand primary navigation' })).toBeVisible();
  await expect(page.getByLabel('Tenant', { exact: true })).toHaveAttribute(
    'title',
    `${longTenantName} · owner`,
  );

  const axeResult = await runAxeCheck(page, testInfo, 'shell-compact-desktop');
  expect(axeResult.unexpected).toHaveLength(0);
  await testInfo.attach('screenshot-shell-compact-desktop', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});

test('mobile shell keeps the same groups, tenant context, and account controls', async ({
  page,
}, testInfo) => {
  await page.setViewportSize(viewports.mobile);
  await setUpPage(page, shellScenario(), 'dark');
  await page.goto(`/tenants/${tenantId}/fleet`);

  await expect(page.getByRole('button', { name: 'Collapse primary navigation' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Open navigation' }).click();

  const dialog = page.getByRole('dialog', { name: 'Navigation' });
  const navigation = dialog.getByRole('navigation', { name: 'Primary navigation' });
  await expect(navigation.getByRole('list', { name: 'Monitor' })).toBeVisible();
  await expect(navigation.getByRole('list', { name: 'Operate' })).toBeVisible();
  await expect(navigation.getByRole('list', { name: 'Configure' })).toBeVisible();
  await expect(dialog.getByLabel('Tenant', { exact: true })).toHaveValue(tenantId);
  await expect(dialog.getByRole('button', { name: 'Sign out' })).toBeVisible();
  await expect(navigation.getByRole('link', { name: 'Settings' })).toBeVisible();
  await expect(navigation.getByRole('link', { name: 'Tenant administration' })).toBeVisible();
  expect((await measureDocumentOverflow(page)).overflowPx).toBe(0);

  const axeResult = await runAxeCheck(page, testInfo, 'shell-mobile');
  expect(axeResult.unexpected).toHaveLength(0);
  await testInfo.attach('screenshot-shell-mobile', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});
