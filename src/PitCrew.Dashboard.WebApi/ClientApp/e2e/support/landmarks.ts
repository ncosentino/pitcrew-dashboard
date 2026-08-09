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

/** Asserts heading levels begin at H1 and never skip a level while descending. */
export async function expectSequentialHeadingOutline(page: Page): Promise<void> {
  const levels = await page
    .locator('h1, h2, h3, h4, h5, h6')
    .evaluateAll((headings) =>
      headings.map((heading) => Number.parseInt(heading.tagName.slice(1), 10)),
    );
  expect(levels.length).toBeGreaterThan(0);
  expect(levels[0]).toBe(1);
  for (let index = 1; index < levels.length; index += 1) {
    expect(
      levels[index] - levels[index - 1],
      `heading level skipped from h${levels[index - 1]} to h${levels[index]}`,
    ).toBeLessThanOrEqual(1);
  }
}

/** Asserts a `<main>` landmark is present, per the shared shell/pre-auth pages. */
export async function expectMainLandmark(page: Page): Promise<void> {
  await expect(page.locator('main')).toHaveCount(1);
}
