import { test, expect, type Page, type TestInfo } from '@playwright/test';

import { viewports } from '../playwright.config';
import { buildSession } from './mocks/fixtures';
import { healthyScenario, tenantId } from './mocks/scenarios';
import { runAxeCheck } from './support/axe';
import { measureDocumentOverflow } from './support/overflow';
import { setUpPage } from './support/session';

const ownerScenario = healthyScenario();

async function expectSettingsEvidence(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  await page.waitForLoadState('networkidle');
  expect((await measureDocumentOverflow(page)).overflowPx, `document overflow on ${name}`).toBe(0);
  const axeResult = await runAxeCheck(page, testInfo, `settings-${name}`);
  expect(
    axeResult.unexpected,
    `unexpected axe violations on ${name}: ${axeResult.unexpected.map((item) => item.id).join(', ')}`,
  ).toHaveLength(0);
  await testInfo.attach(`screenshot-settings-${name}`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
}

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
        .getByRole('navigation', { name: 'Primary navigation' })
        .getByRole('link', { name: 'Enrollment' }),
    ).toHaveCount(0);
    await expect(
      page
        .getByRole('navigation', { name: 'Primary navigation' })
        .getByRole('link', { name: 'Diagnostics' }),
    ).toHaveCount(0);
    await expect(
      page
        .getByRole('navigation', { name: 'Tenant settings' })
        .getByRole('link', { name: 'Access' }),
    ).toHaveAttribute('aria-current', 'page');
    await expect(page.getByRole('region', { name: 'Administration context' })).toContainText(
      'Owner',
    );

    await page.getByRole('button', { name: 'Remove' }).first().click();
    const dialog = page.getByRole('alertdialog');
    await expect(dialog).toContainText('The user will lose all access to this tenant.');
    await dialog.getByRole('button', { name: 'Cancel' }).click();

    await page.getByText('Add a member', { exact: true }).click();
    await page.getByRole('button', { name: 'Add member' }).click();
    await expect(page.getByRole('alertdialog')).toContainText(
      'This does not grant system-administrator access.',
    );
  });

  test('owner sees the stable tenant ID as copyable metadata', async ({ page }) => {
    await setUpPage(page, ownerScenario, 'light');
    await page.goto(`/tenants/${tenantId}/settings/general`);

    await expect(page.getByRole('heading', { level: 2, name: 'Tenant identity' })).toBeVisible();
    await expect(page.getByTestId('copyable-id-value')).toHaveText(tenantId);
    await expect(page.getByRole('button', { name: 'Copy tenant ID' })).toBeVisible();
  });

  test('administrator sees enrollment and diagnostics without owner tabs', async ({ page }) => {
    await setUpPage(page, roleScenario('administrator'), 'dark');
    await page.goto(`/tenants/${tenantId}/settings/diagnostics`);
    await page.waitForLoadState('networkidle');

    const navigation = page.getByRole('navigation', { name: 'Tenant settings' });
    await expect(
      page
        .getByRole('navigation', { name: 'Primary navigation' })
        .getByRole('link', { name: 'Settings' }),
    ).toHaveAttribute('aria-current', 'page');
    await expect(navigation.getByRole('link', { name: 'Enrollment' })).toBeVisible();
    await expect(navigation.getByRole('link', { name: 'Diagnostics' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expect(navigation.getByRole('link', { name: 'General' })).toHaveCount(0);
    await expect(navigation.getByRole('link', { name: 'Access' })).toHaveCount(0);

    await expect(page.getByRole('heading', { name: 'Read-only diagnostic access' })).toBeVisible();
    await expect(page.getByLabel('Credential label')).toBeHidden();
    await page.getByText('Create a credential', { exact: true }).click();
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

test('one-time enrollment value is focused, copyable, and explicitly cleared', async ({
  page,
}, testInfo) => {
  await page.setViewportSize(viewports.mobile);
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
  await setUpPage(page, ownerScenario, 'light');
  await page.goto(`/tenants/${tenantId}/settings/enrollment`);

  await expect(
    page.getByRole('navigation', { name: 'Tenant settings' }).getByRole('link', {
      name: /Enrollment/,
    }),
  ).toBeInViewport({ ratio: 1 });
  await page.getByRole('button', { name: 'Create one-time code' }).click();

  const result = page.getByRole('region', { name: 'Enrollment code ready' });
  await expect(result).toBeFocused();
  await expect(result).toContainText('placeholder-enrollment-code');
  await result.getByRole('button', { name: 'Copy value' }).click();
  await expect(result.getByRole('button', { name: 'Copied' })).toBeVisible();
  await result.getByRole('button', { name: 'Clear one-time value' }).click();
  await page
    .getByRole('alertdialog', { name: 'Clear this one-time value?' })
    .getByRole('button', { name: 'Clear value' })
    .click();
  await expect(result).toHaveCount(0);

  await expectSettingsEvidence(page, testInfo, 'enrollment-one-time-mobile');
});

test('rotated diagnostic value replaces metadata with a focused one-time result', async ({
  page,
}, testInfo) => {
  await page.setViewportSize(viewports.desktop);
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
  await setUpPage(page, ownerScenario, 'dark');
  await page.goto(`/tenants/${tenantId}/settings/diagnostics`);

  await page.getByRole('button', { name: 'Rotate' }).click();
  await page
    .getByRole('alertdialog', { name: 'Rotate "Read-only fleet audit"?' })
    .getByRole('button', { name: 'Rotate credential' })
    .click();

  const result = page.getByRole('region', { name: 'Diagnostic credential ready' });
  await expect(result).toBeFocused();
  await expect(result).toContainText('placeholder-diagnostic-credential-value');
  await expect(page.getByText('Read-only fleet audit')).toBeVisible();

  await expectSettingsEvidence(page, testInfo, 'diagnostic-rotation-dark');
});

test('long membership and diagnostic records remain contained at 320px', async ({
  page,
}, testInfo) => {
  const longText = 'operator'.repeat(18);
  const scenario = {
    ...ownerScenario,
    tenantMembers: ownerScenario.tenantMembers?.map((member, index) =>
      index === 0
        ? {
            ...member,
            user: {
              ...member.user,
              displayName: longText,
              githubLogin: longText,
            },
          }
        : member,
    ),
    diagnosticCredentials: ownerScenario.diagnosticCredentials?.map((credential) => ({
      ...credential,
      label: longText,
      profileIds: [longText],
    })),
  };

  await page.setViewportSize(viewports.narrow);
  await setUpPage(page, scenario, 'light');
  await page.goto(`/tenants/${tenantId}/settings/access`);
  await expect(page.getByText(longText, { exact: true }).first()).toBeVisible();
  await expectSettingsEvidence(page, testInfo, 'access-long-content-narrow');

  await page.goto(`/tenants/${tenantId}/settings/diagnostics`);
  await expect(page.getByText(longText, { exact: true }).first()).toBeVisible();
  await expectSettingsEvidence(page, testInfo, 'diagnostics-long-content-narrow');
});
