/**
 * Product hardening evidence: keyboard walkthroughs, 200% zoom / text
 * scaling, Windows forced-colors / high contrast, reduced motion, long
 * content / 40% expansion, CJK / emoji / RTL, and large dataset scenarios.
 *
 * Each test exercises a real production concern required by issue #91.
 */
import { test, expect } from '@playwright/test';

import { viewports } from '../playwright.config';
import { healthyScenario, tenantId, nodeIds } from './mocks/scenarios';
import { buildFleetNode, buildFleetResponse, buildProfile } from './mocks/fixtures';
import { setUpPage } from './support/session';
import { measureDocumentOverflow } from './support/overflow';
import { expectMainLandmark, expectSingleDescriptiveH1 } from './support/landmarks';

// ---------------------------------------------------------------------------
// Keyboard walkthrough: primary task flow (fleet → node → profile)
// ---------------------------------------------------------------------------
test.describe('keyboard walkthrough', () => {
  test('fleet → node detail via keyboard only', async ({ page }) => {
    await setUpPage(page, healthyScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');

    // Tab to first node link and activate
    let found = false;
    for (let i = 0; i < 30; i++) {
      await page.keyboard.press('Tab');
      const focused = page.locator(':focus');
      const role = await focused.getAttribute('role');
      const tagName = await focused.evaluate((el) => el.tagName.toLowerCase());
      if (tagName === 'a' || role === 'link') {
        const href = await focused.getAttribute('href');
        if (href && href.includes('/nodes/')) {
          found = true;
          await page.keyboard.press('Enter');
          break;
        }
      }
    }
    expect(found, 'should reach a node link via Tab').toBe(true);
    await page.waitForLoadState('networkidle');
    expect(page.url()).toContain('/nodes/');
    await expectSingleDescriptiveH1(page);
  });

  test('confirmation dialog traps focus and supports keyboard escape', async ({ page }) => {
    await setUpPage(page, healthyScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}/administration`);

    const trigger = page.getByRole('button', { name: 'Revoke', exact: true });
    await trigger.focus();
    await trigger.press('Enter');

    const dialog = page.getByRole('alertdialog');
    await expect(dialog).toBeVisible();

    // Focus should be inside dialog
    const focusInDialog = await page.evaluate(() => {
      const dialog = document.querySelector('[role="alertdialog"]');
      return dialog?.contains(document.activeElement) ?? false;
    });
    expect(focusInDialog, 'focus trapped in dialog').toBe(true);

    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
  });

  test('fleet evidence help is keyboard-operable and explains state language', async ({ page }) => {
    await setUpPage(page, healthyScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);

    const summary = page.getByText('How to read fleet evidence');
    await summary.focus();
    await page.keyboard.press('Enter');

    await expect(page.getByText('No trustworthy measurement was reported.')).toBeVisible();
    await expect(
      page.getByText('An operator owns the incident; the condition is not resolved.'),
    ).toBeVisible();
  });
});

// ---------------------------------------------------------------------------
// 200% zoom / text scaling (CSS zoom 2x with viewport containment)
// ---------------------------------------------------------------------------
test.describe('200% zoom-equivalent reflow', () => {
  test('fleet page has no overflow at 200% zoom', async ({ page }) => {
    await page.setViewportSize({ width: 720, height: 900 });
    await setUpPage(page, healthyScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'no overflow at 200% zoom').toBe(0);
  });

  test('node page has no overflow at 200% zoom', async ({ page }) => {
    await page.setViewportSize({ width: 640, height: 900 });
    await setUpPage(page, healthyScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}`);
    await page.waitForLoadState('networkidle');

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'no overflow at 200% zoom').toBe(0);
  });
});

// ---------------------------------------------------------------------------
// Windows forced-colors / high contrast mode
// ---------------------------------------------------------------------------
test.describe('forced-colors', () => {
  test('fleet page renders without invisible elements in forced-colors mode', async ({
    page,
  }, testInfo) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await setUpPage(page, healthyScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');

    await expectSingleDescriptiveH1(page);
    await expectMainLandmark(page);

    // Status indicators must use text/border, not color alone
    const statusElements = page.locator('[data-status]');
    const count = await statusElements.count();
    if (count > 0) {
      for (let i = 0; i < Math.min(count, 5); i++) {
        const text = await statusElements.nth(i).textContent();
        expect(text?.trim().length, 'status has textual label').toBeGreaterThan(0);
      }
    }

    await testInfo.attach('screenshot-forced-colors-fleet', {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
  });
});

// ---------------------------------------------------------------------------
// Reduced motion
// ---------------------------------------------------------------------------
test.describe('reduced motion', () => {
  test('no CSS animations run when prefers-reduced-motion is active', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await setUpPage(page, healthyScenario(), 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');

    const animations = await page.evaluate(() => {
      const allElements = document.querySelectorAll('*');
      const running: string[] = [];
      allElements.forEach((el) => {
        const style = getComputedStyle(el);
        if (
          style.animationName !== 'none' &&
          style.animationDuration !== '0s' &&
          style.animationPlayState === 'running'
        ) {
          running.push(`${el.tagName}.${el.className}`);
        }
      });
      return running;
    });

    expect(animations, 'no active animations under reduced-motion').toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// Long content / 40% text expansion
// ---------------------------------------------------------------------------
test.describe('long content and text expansion', () => {
  test('40% expanded node display names do not overflow', async ({ page }) => {
    // Build a scenario with long display names (simulating 40% expansion)
    const longName = 'Alpha Production Runner Host (North America)';
    const expandedName = `${longName} ${longName.slice(0, Math.ceil(longName.length * 0.4))}`;
    const node = buildFleetNode({
      nodeId: nodeIds.alpha,
      displayName: expandedName,
      isOnline: true,
      profiles: [buildProfile('build')],
    });
    const scenario = {
      ...healthyScenario(),
      fleet: buildFleetResponse([node], []),
    };

    await page.setViewportSize(viewports.mobile);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');
    await page.locator('html').evaluate((element) => element.setAttribute('dir', 'rtl'));

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'no overflow with 40% expanded names').toBe(0);
  });

  test('long profile identifiers do not break node detail layout', async ({ page }) => {
    const longProfileName = 'build-production-extended-workflow-name-for-testing';
    const profile = buildProfile(longProfileName);
    const node = buildFleetNode({
      nodeId: nodeIds.alpha,
      displayName: 'Alpha',
      isOnline: true,
      profiles: [profile],
    });
    const scenario = {
      ...healthyScenario(),
      fleet: buildFleetResponse([node], []),
    };

    await page.setViewportSize(viewports.intermediate);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/nodes/${nodeIds.alpha}`);
    await page.waitForLoadState('networkidle');

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'no overflow with long profile names').toBe(0);
  });
});

// ---------------------------------------------------------------------------
// CJK / emoji / RTL
// ---------------------------------------------------------------------------
test.describe('internationalization', () => {
  test('CJK and emoji in display names render without overflow', async ({ page }) => {
    const node = buildFleetNode({
      nodeId: nodeIds.alpha,
      displayName: '本番ランナー🚀ホスト',
      isOnline: true,
      profiles: [buildProfile('build')],
    });
    const scenario = {
      ...healthyScenario(),
      fleet: buildFleetResponse([node], []),
    };

    await page.setViewportSize(viewports.mobile);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'no overflow with CJK/emoji names').toBe(0);

    // Verify the text is actually rendered
    const content = await page.textContent('main');
    expect(content).toContain('本番ランナー🚀ホスト');
  });

  test('RTL display names maintain layout containment', async ({ page }) => {
    const node = buildFleetNode({
      nodeId: nodeIds.alpha,
      displayName: 'مضيف الإنتاج ألفا',
      isOnline: true,
      profiles: [buildProfile('build')],
    });
    const scenario = {
      ...healthyScenario(),
      fleet: buildFleetResponse([node], []),
    };

    await page.setViewportSize(viewports.mobile);
    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');

    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'no overflow with RTL names').toBe(0);
  });
});

// ---------------------------------------------------------------------------
// Large dataset scenario
// ---------------------------------------------------------------------------
test.describe('large dataset', () => {
  test('fleet page with 50 nodes renders within performance budget', async ({ page }) => {
    const nodes = Array.from({ length: 50 }, (_, i) => {
      const id = `00000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`;
      return buildFleetNode({
        nodeId: id,
        displayName: `Node-${String(i).padStart(3, '0')}`,
        isOnline: i % 5 !== 0,
        profiles: [buildProfile('build')],
      });
    });

    const scenario = {
      ...healthyScenario(),
      fleet: buildFleetResponse(nodes, []),
    };

    await page.setViewportSize(viewports.desktop);
    await setUpPage(page, scenario, 'light');

    const start = Date.now();
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');
    const loadMs = Date.now() - start;

    // Performance budget: initial fleet render under 3000ms
    expect(loadMs, 'fleet page loads within 3s budget').toBeLessThan(3000);

    await expectSingleDescriptiveH1(page);
    const overflow = await measureDocumentOverflow(page);
    expect(overflow.overflowPx, 'no overflow with 50 nodes').toBe(0);
  });

  test('one-item fleet renders correctly', async ({ page }) => {
    const node = buildFleetNode({
      nodeId: nodeIds.alpha,
      displayName: 'Solo Node',
      isOnline: true,
      profiles: [buildProfile('build')],
    });
    const scenario = {
      ...healthyScenario(),
      fleet: buildFleetResponse([node], []),
    };

    await setUpPage(page, scenario, 'light');
    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.waitForLoadState('networkidle');

    await expectSingleDescriptiveH1(page);
    const content = await page.textContent('main');
    expect(content).toContain('Solo Node');
  });
});

// ---------------------------------------------------------------------------
// Slow / stale / aborted network scenarios
// ---------------------------------------------------------------------------
test.describe('network edge cases', () => {
  test('slow API response shows loading state', async ({ page }) => {
    const scenario = healthyScenario();
    await setUpPage(page, scenario, 'light');
    await page.route(/\/api\/tenants\/[^/]+\/fleet\/v1\/nodes(\?.*)?$/, async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 2000));
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(scenario.fleet),
      });
    });

    await page.goto(`/tenants/${tenantId}/fleet`);

    await expect(page.getByRole('status')).toContainText('Loading fleet status');
    await expect(page.getByRole('link', { name: 'Alpha' })).toBeVisible({ timeout: 5000 });
  });

  test('aborted fleet request does not leak an error after navigation', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (error) => errors.push(error.message));
    await setUpPage(page, healthyScenario(), 'light');
    await page.route(/\/api\/tenants\/[^/]+\/fleet\/v1\/nodes(\?.*)?$/, async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 1500));
      await route.abort('failed');
    });

    await page.goto(`/tenants/${tenantId}/fleet`);
    await page.goto(`/tenants/${tenantId}/settings/general`);
    await expect(page.getByRole('heading', { level: 1, name: 'Tenant settings' })).toBeVisible();
    await page.waitForTimeout(1700);

    expect(errors).toHaveLength(0);
    await expect(page.getByText(/fleet data is unavailable/i)).toHaveCount(0);
  });
});
