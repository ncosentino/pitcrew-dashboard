/**
 * Collects lightweight navigation timing metrics during the browser
 * evidence run. Each metric is written to its own file (named after the
 * test) rather than appended to one shared file, so parallel Playwright
 * workers never race on a single read-modify-write.
 */
import type { Page } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const metricsRoot = path.join(process.cwd(), 'e2e', '.artifacts', 'metrics');

export interface RouteMetric {
  readonly test: string;
  readonly route: string;
  readonly viewport: string;
  readonly theme: string;
  readonly domContentLoadedMs: number;
  readonly loadEventMs: number;
}

function sanitizeName(name: string): string {
  return name.replace(/[^a-z0-9-]+/gi, '-').toLowerCase();
}

export async function measureNavigationTiming(page: Page): Promise<{
  readonly domContentLoadedMs: number;
  readonly loadEventMs: number;
}> {
  return page.evaluate(() => {
    const [entry] = performance.getEntriesByType('navigation') as PerformanceNavigationTiming[];
    if (!entry) return { domContentLoadedMs: 0, loadEventMs: 0 };
    return {
      domContentLoadedMs: Math.round(entry.domContentLoadedEventEnd),
      loadEventMs: Math.round(entry.loadEventEnd),
    };
  });
}

/** Writes one metric record as its own artifact file (bounded, deterministic, race-free). */
export async function recordMetric(metric: RouteMetric): Promise<void> {
  await mkdir(metricsRoot, { recursive: true });
  const fileName = `${sanitizeName(metric.test)}.json`;
  await writeFile(path.join(metricsRoot, fileName), JSON.stringify(metric, null, 2), 'utf8');
}
