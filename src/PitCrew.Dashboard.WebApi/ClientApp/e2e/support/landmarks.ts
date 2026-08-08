/**
 * Structural assertions shared across the browser evidence spec files:
 * exactly one descriptive `<h1>`, a `<main>` landmark, and accessible names
 * on interactive controls.
 */
import { expect, type Page } from '@playwright/test';

/** Asserts the page renders exactly one non-empty `<h1>`. */
export async function expectSingleDescriptiveH1(page: Page): Promise<void> {
  const headings = page.locator('h1');
  await expect(headings).toHaveCount(1);
  const text = (await headings.first().textContent())?.trim() ?? '';
  expect(text.length).toBeGreaterThan(0);
}

/** Asserts a `<main>` landmark is present, per the shared shell/pre-auth pages. */
export async function expectMainLandmark(page: Page): Promise<void> {
  await expect(page.locator('main')).toHaveCount(1);
}
