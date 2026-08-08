/**
 * Deterministic session/theme setup for the browser evidence suite.
 *
 * Playwright's `page.addInitScript` runs before any page script, so setting
 * `localStorage` here lets every navigation start with the requested theme
 * already applied instead of racing `initializeColorTheme()`.
 */
import type { Page } from '@playwright/test';

import { colorThemeStorageKey, type ColorTheme } from '../../src/core/theme/colorTheme';
import { installMockApi, type MockApiOptions } from '../mocks/router';

/** Installs the mock API and pins the color theme before any navigation. */
export async function setUpPage(
  page: Page,
  options: MockApiOptions,
  theme: ColorTheme = 'light',
): Promise<void> {
  await installMockApi(page, options);
  await page.addInitScript(
    ([key, value]) => {
      globalThis.localStorage.setItem(key, value);
    },
    [colorThemeStorageKey, theme] as const,
  );
}
