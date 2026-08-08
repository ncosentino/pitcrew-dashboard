/**
 * Asserts the document does not overflow its own viewport horizontally.
 * ADR-0007 documents this as a known pre-existing defect on some routes; the
 * spec files decide (per-route) whether to record a failure as baseline
 * evidence rather than let it hard-fail the advisory suite.
 */
import type { Page } from '@playwright/test';

export interface OverflowMeasurement {
  readonly scrollWidth: number;
  readonly clientWidth: number;
  readonly overflowPx: number;
}

export async function measureDocumentOverflow(page: Page): Promise<OverflowMeasurement> {
  return page.evaluate(() => {
    const root = document.documentElement;
    return {
      scrollWidth: root.scrollWidth,
      clientWidth: root.clientWidth,
      overflowPx: Math.max(0, root.scrollWidth - root.clientWidth),
    };
  });
}
