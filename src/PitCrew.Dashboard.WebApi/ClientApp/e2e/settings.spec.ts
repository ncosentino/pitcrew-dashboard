import { test, expect } from '@playwright/test';

import { buildSession } from './mocks/fixtures';
import { healthyScenario, tenantId } from './mocks/scenarios';
import { setUpPage } from './support/session';

const ownerScenario = healthyScenario();

function roleScenario(role: 'administrator' | 'viewer') {
  return {
    ...ownerScenario,
    session: buildSession(role),
  };
}

test.describe('settings roles and navigation', () => {
  test('owner sees active primary and settings navigation on access', async ({ page }) => {
    await setUpPage(page, ownerScenario, 'light');
    await page.goto(`/tenants/${tenantId}/settings/access`);
    await page.waitForLoadState('networkidle');

    await expect(page).toHaveTitle('Tenant access · PitCrew Dashboard');
    await expect(
      page
        .getByRole('navigation', { name: 'Primary navigation' })
        .getByRole('link', { name: 'Settings' }),
    ).toHaveAttribute('aria-current', 'page');
    await expect(
      page
        .getByRole('navigation', { name: 'Tenant settings' })
        .getByRole('link', { name: 'Access' }),
    ).toHaveAttribute('aria-current', 'page');

    await page.getByRole('button', { name: 'Remove' }).first().click();
    const dialog = page.getByRole('alertdialog');
    await expect(dialog).toContainText('The user will lose all access to this tenant.');
    await dialog.getByRole('button', { name: 'Cancel' }).click();

    await page.getByRole('button', { name: 'Add member' }).click();
    await expect(page.getByRole('alertdialog')).toContainText(
      'This does not grant system-administrator access.',
    );
  });

  test('owner sees the stable tenant ID as copyable metadata', async ({ page }) => {
    await setUpPage(page, ownerScenario, 'light');
    await page.goto(`/tenants/${tenantId}/settings/general`);

    await expect(page.getByRole('heading', { level: 2, name: 'Tenant settings' })).toBeVisible();
    await expect(page.getByTestId('copyable-id-value')).toHaveText(tenantId);
    await expect(page.getByRole('button', { name: 'Copy tenant ID' })).toBeVisible();
  });

  test('administrator sees enrollment and diagnostics without owner tabs', async ({ page }) => {
    await setUpPage(page, roleScenario('administrator'), 'dark');
    await page.goto(`/tenants/${tenantId}/settings/diagnostics`);
    await page.waitForLoadState('networkidle');

    const navigation = page.getByRole('navigation', { name: 'Tenant settings' });
    await expect(navigation.getByRole('link', { name: 'Enrollment' })).toBeVisible();
    await expect(navigation.getByRole('link', { name: 'Diagnostics' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expect(navigation.getByRole('link', { name: 'General' })).toHaveCount(0);
    await expect(navigation.getByRole('link', { name: 'Access' })).toHaveCount(0);

    await expect(page.getByLabel('Credential label')).toBeVisible();
    await page.getByRole('button', { name: 'Rotate' }).click();
    await expect(page.getByRole('alertdialog')).toContainText(
      'The previous value becomes invalid immediately.',
    );
  });

  test('viewer receives an explicit permission state', async ({ page }) => {
    await setUpPage(page, roleScenario('viewer'), 'light');
    await page.goto(`/tenants/${tenantId}/settings/diagnostics`);

    await expect(page.getByText('Insufficient tenant role')).toBeVisible();
    await expect(page.getByText(/requires the administrator role/i)).toBeVisible();
  });

  test('system administrator with tenant ownership retains settings access', async ({ page }) => {
    await setUpPage(
      page,
      {
        ...ownerScenario,
        session: buildSession('owner', { isSystemAdministrator: true }),
      },
      'light',
    );
    await page.goto(`/tenants/${tenantId}/settings/general`);

    await expect(page.getByRole('heading', { level: 1, name: 'Tenant settings' })).toBeVisible();
  });

  test('tenantless user receives the no-access shell', async ({ page }) => {
    await setUpPage(
      page,
      {
        ...ownerScenario,
        session: buildSession(null),
      },
      'light',
    );
    await page.goto('/');

    await expect(page.getByRole('heading', { level: 1, name: 'No tenant access' })).toBeVisible();
  });
});

test('session bootstrap exposes the branded loading shell', async ({ page }) => {
  await setUpPage(page, ownerScenario, 'light');
  await page.route('**/api/session', async () => await new Promise<void>(() => undefined));
  await page.goto('/');

  await expect(
    page.getByRole('heading', { level: 1, name: 'Opening PitCrew Dashboard' }),
  ).toBeVisible();
  await expect(page.getByRole('status')).toHaveText('Loading dashboard session…');
});
