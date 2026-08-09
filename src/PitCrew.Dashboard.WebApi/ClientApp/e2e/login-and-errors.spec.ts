/**
 * Login and error-route coverage.
 *
 * Both pre-authentication routes select an explicit H1 through
 * `CardTitle.as`, preserving the same heading contract as authenticated
 * routes without depending on `AuthenticatedShell`.
 */
import { test, expect } from '@playwright/test';

import { sessionErrorScenario, unauthenticatedScenario } from './mocks/scenarios';
import { setUpPage } from './support/session';
import {
  runAxeCheck,
  KNOWN_BASELINE_COLOR_CONTRAST_COLOR_PAIRS,
  KNOWN_BASELINE_COLOR_CONTRAST_NODE_HTML,
} from './support/axe';
import { measureDocumentOverflow } from './support/overflow';
import {
  expectMainLandmark,
  expectSequentialHeadingOutline,
  expectSingleDescriptiveH1,
} from './support/landmarks';

test.describe('login page', () => {
  test('renders a sign-in main landmark with no document overflow', async ({ page }, testInfo) => {
    await setUpPage(page, unauthenticatedScenario(), 'light');
    await page.goto('/');

    await expectMainLandmark(page);
    await expect(page.getByRole('link', { name: 'Sign in with GitHub' })).toBeVisible();

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'login page document overflow').toBe(0);

    await testInfo.attach('screenshot-login', {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
  });

  test('has a single descriptive H1 and no unexpected axe violations', async ({
    page,
  }, testInfo) => {
    await setUpPage(page, unauthenticatedScenario(), 'light');
    await page.goto('/');

    await expectSingleDescriptiveH1(page);
    await expectSequentialHeadingOutline(page);

    const axeResult = await runAxeCheck(page, testInfo, 'login');
    const foundModerateHeadingFinding = axeResult.all.some(
      (violation) => violation.id === 'page-has-heading-one',
    );
    expect(
      foundModerateHeadingFinding,
      'login page must not regress to the page-has-heading-one finding',
    ).toBe(false);
    expect(
      axeResult.unexpected,
      `unexpected serious/critical axe violations on login page: ${axeResult.unexpected
        .map((violation) => violation.id)
        .join(', ')}`,
    ).toHaveLength(0);
  });
});

test.describe('session bootstrap failure (error route)', () => {
  test('surfaces the retry alert instead of the login page', async ({ page }, testInfo) => {
    await setUpPage(page, sessionErrorScenario(), 'light');
    await page.goto('/');

    // `SessionProvider` treats any non-401 `/api/session` failure as its
    // `'error'` status, and `SessionBoundary` renders this `role="alert"`
    // surface instead of `LoginPage` (which only appears on 401).
    const alert = page.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(alert.getByText('Dashboard session is unavailable')).toBeVisible();
    const retryButton = alert.getByRole('button', { name: 'Retry session' });
    await expect(retryButton).toBeVisible();
    await expect(retryButton).toBeEnabled();

    await expectMainLandmark(page);
    await expectSingleDescriptiveH1(page);
    await expectSequentialHeadingOutline(page);

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'session-error route document overflow').toBe(0);

    const axeResult = await runAxeCheck(page, testInfo, 'session-error-route');
    expect(
      axeResult.unexpected,
      `unexpected serious/critical axe violations on session-error route: ${axeResult.unexpected
        .map((violation) => violation.id)
        .join(', ')}`,
    ).toHaveLength(0);

    await testInfo.attach('screenshot-session-error', {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
  });
});

test.describe('sanity for baseline allowlist itself', () => {
  test('the color-contrast node-html allowlist is empty after issue #86 contrast repairs', () => {
    expect(KNOWN_BASELINE_COLOR_CONTRAST_NODE_HTML.size).toBe(0);
  });

  test('the color-contrast color-pair allowlist is empty after issue #86 contrast repairs', () => {
    expect(KNOWN_BASELINE_COLOR_CONTRAST_COLOR_PAIRS.size).toBe(0);
  });
});
